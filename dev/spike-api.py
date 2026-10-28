#!/usr/bin/env python3
"""Server-side cases of the Mechanism A spike (docs/spike-mechanism-a.md), against the dev server.

Run after dev/setup-server.py. Calls PlaybackInfo as the test users with a direct-play profile
and with a low bitrate limit, starts the transcode the plugin's TranscodingUrl points to and
reads the stream map back from the server's FFmpeg log. Prints one line per check and exits
non-zero when one fails.

    python3 dev/spike-api.py [http://localhost:8096]
"""

import json
import re
import sys
import threading
import time
import urllib.parse
import urllib.request

SERVER = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8096").rstrip("/")

# A client that direct-plays H.264 / AAC in MKV and transcodes to HLS otherwise, as Jellyfin Web does.
PROFILE = {
    "Name": "spike",
    "MaxStreamingBitrate": 120_000_000,
    "DirectPlayProfiles": [{"Container": "mkv,mp4", "Type": "Video", "VideoCodec": "h264", "AudioCodec": "aac"}],
    "TranscodingProfiles": [{
        "Container": "ts", "Type": "Video", "VideoCodec": "h264", "AudioCodec": "aac",
        "Protocol": "hls", "Context": "Streaming", "MaxAudioChannels": "2", "BreakOnNonKeyFrames": True,
    }],
}
LOW_BITRATE = 100_000

failures = []


def call(method, path, token, body=None, raw=False):
    auth = f'MediaBrowser Client="spike-api", Device="script", DeviceId="spike-api-{token[:8]}", Version="1", Token="{token}"'
    url = path if path.startswith("http") else SERVER + path
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Authorization", auth)
    if data is not None:
        request.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(request) as response:
        text = response.read()
        return text if raw else (json.loads(text) if text else None)


def login(name):
    request = urllib.request.Request(
        SERVER + "/Users/AuthenticateByName",
        data=json.dumps({"Username": name, "Pw": name}).encode(), method="POST")
    request.add_header("Content-Type", "application/json")
    request.add_header("Authorization", f'MediaBrowser Client="spike-api", Device="script", DeviceId="spike-api-{name}", Version="1"')
    with urllib.request.urlopen(request) as response:
        result = json.load(response)
        return result["User"]["Id"], result["AccessToken"]


def check(name, ok, detail):
    print(f"{'PASS' if ok else 'FAIL'}  {name}: {detail}")
    if not ok:
        failures.append(name)


def playback_info(token, item_id, **body):
    return call("POST", f"/Items/{item_id}/PlaybackInfo", token, {"DeviceProfile": PROFILE, **body})


def audio_language(source, index):
    return next((s.get("Language") for s in source["MediaStreams"] if s["Index"] == index), None)


def describe(source):
    index = source.get("DefaultAudioStreamIndex")
    url = source.get("TranscodingUrl")
    in_url = re.search(r"AudioStreamIndex=(\d+)", url or "")
    return (f"{source['Name']}: DefaultAudioStreamIndex {index} ({audio_language(source, index)})"
            + (f", TranscodingUrl AudioStreamIndex={in_url.group(1)}" if url else ", direct play"))


def find_item(token, user_id, name):
    items = call("GET", f"/Items?userId={user_id}&Recursive=true&IncludeItemTypes=Movie,Episode&searchTerm="
                 + urllib.parse.quote(name), token)["Items"]
    return next(item["Id"] for item in items if item["Name"] == name)


def transcode_map(token, admin, source):
    """Starts the HLS transcode behind TranscodingUrl and returns the audio input FFmpeg mapped."""
    master = call("GET", source["TranscodingUrl"], token, raw=True).decode()
    variant = next(line for line in master.splitlines() if line and not line.startswith("#"))
    base = SERVER + source["TranscodingUrl"].split("?")[0].rsplit("/", 1)[0] + "/"
    playlist = call("GET", base + variant, token, raw=True).decode()
    segment = next(line for line in playlist.splitlines() if line and not line.startswith("#"))
    call("GET", base + segment, token, raw=True)
    play_session = re.search(r"PlaySessionId=([^&]+)", source["TranscodingUrl"]).group(1)
    for _ in range(20):
        logs = call("GET", "/System/Logs", admin)
        for log in sorted(logs, key=lambda entry: entry["DateModified"], reverse=True):
            if log["Name"].startswith("FFmpeg.Transcode"):
                text = call("GET", "/System/Logs/Log?name=" + urllib.parse.quote(log["Name"]), admin, raw=True).decode()
                if play_session in text or source["Id"] in text:
                    # The command line maps the video first, then the audio: "-map 0:0 -map 0:1".
                    maps = re.findall(r"-map 0:(\d+)", text)
                    return int(maps[1]) if len(maps) > 1 else None
        time.sleep(1)
    return None


def main():
    _, admin = login("admin")
    aiko_id, aiko = login("aiko")
    bob_id, bob = login("bob")
    server = call("GET", "/System/Info", admin)
    print(f"Jellyfin {server['Version']} at {SERVER}; aiko is mapped to jpn, bob is unmapped\n")

    movie = find_item(aiko, aiko_id, "Spike Movie (2026)")
    versions = find_item(aiko, aiko_id, "Spike Versions (2026)")
    episode = find_item(aiko, aiko_id, "Spike Show - S01E02")

    # Direct play
    source = playback_info(aiko, movie, UserId=aiko_id)["MediaSources"][0]
    check("direct play, aiko", audio_language(source, source["DefaultAudioStreamIndex"]) == "jpn", describe(source))
    source = playback_info(bob, movie, UserId=bob_id)["MediaSources"][0]
    check("direct play, bob (stock)", audio_language(source, source["DefaultAudioStreamIndex"]) == "eng", describe(source))

    # Legacy GET endpoint
    source = call("GET", f"/Items/{movie}/PlaybackInfo?userId={aiko_id}", aiko)["MediaSources"][0]
    check("GET PlaybackInfo, aiko", audio_language(source, source["DefaultAudioStreamIndex"]) == "jpn", describe(source))

    # Forced transcode: the URL carries the plugin's index, and FFmpeg maps that stream.
    source = playback_info(aiko, movie, UserId=aiko_id, MaxStreamingBitrate=LOW_BITRATE)["MediaSources"][0]
    in_url = re.search(r"AudioStreamIndex=(\d+)", source.get("TranscodingUrl") or "")
    check("transcode URL, aiko", in_url is not None and audio_language(source, int(in_url.group(1))) == "jpn", describe(source))
    if in_url:
        mapped = transcode_map(aiko, admin, source)
        check("transcode FFmpeg map, aiko", mapped is not None and audio_language(source, mapped) == "jpn",
              f"FFmpeg mapped input stream 0:{mapped} ({audio_language(source, mapped)})")
    source = playback_info(bob, movie, UserId=bob_id, MaxStreamingBitrate=LOW_BITRATE)["MediaSources"][0]
    in_url = re.search(r"AudioStreamIndex=(\d+)", source.get("TranscodingUrl") or "")
    check("transcode URL, bob (stock)", in_url is not None and audio_language(source, int(in_url.group(1))) == "eng", describe(source))

    # Two versions: the one with a Japanese track first for aiko, the server's order for bob.
    sources = playback_info(aiko, versions, UserId=aiko_id)["MediaSources"]
    check("two versions, aiko", sources[0]["Name"].endswith("VO"), " | ".join(describe(s) for s in sources))
    sources = playback_info(bob, versions, UserId=bob_id)["MediaSources"]
    check("two versions, bob (stock)", sources[0]["Name"].endswith("VF"), " | ".join(describe(s) for s in sources))

    # The client's own choice (manual switch, web's next-episode carry-over) is left alone. The server only honours
    # an AudioStreamIndex sent with its MediaSourceId, as Jellyfin Web does.
    source_id = playback_info(aiko, episode, UserId=aiko_id)["MediaSources"][0]["Id"]
    source = playback_info(aiko, episode, UserId=aiko_id, AudioStreamIndex=1, MediaSourceId=source_id)["MediaSources"][0]
    check("explicit AudioStreamIndex in body, aiko", source["DefaultAudioStreamIndex"] == 1, describe(source))
    source = call("POST", f"/Items/{episode}/PlaybackInfo?audioStreamIndex=1&mediaSourceId={source_id}", aiko,
                  {"DeviceProfile": PROFILE, "UserId": aiko_id, "MaxStreamingBitrate": LOW_BITRATE})["MediaSources"][0]
    check("explicit audioStreamIndex in query, transcode, aiko",
          source["DefaultAudioStreamIndex"] == 1 and "AudioStreamIndex=1" in source["TranscodingUrl"], describe(source))

    # Track order differs between episodes: the language follows, not the index.
    for name in ("Spike Show - S01E01", "Spike Show - S01E02"):
        source = playback_info(aiko, find_item(aiko, aiko_id, name), UserId=aiko_id)["MediaSources"][0]
        check(f"{name}, aiko", audio_language(source, source["DefaultAudioStreamIndex"]) == "jpn", describe(source))

    # Two users on the same episode at the same time.
    results = {}

    def play(name, user_id, token):
        results[name] = [playback_info(token, episode, UserId=user_id)["MediaSources"][0] for _ in range(10)]

    threads = [threading.Thread(target=play, args=args) for args in (("aiko", aiko_id, aiko), ("bob", bob_id, bob))]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    for name, expected in (("aiko", "jpn"), ("bob", "eng")):
        languages = {audio_language(s, s["DefaultAudioStreamIndex"]) for s in results[name]}
        check(f"parallel, {name}", languages == {expected}, f"10 requests, languages {sorted(languages)}")

    print(f"\n{len(failures)} failed" if failures else "\nAll checks passed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
