# Spike: Mechanism A on Jellyfin 12.4

SPEC.md milestone 1: prove that rewriting the PlaybackInfo response (Mechanism A) makes clients start
on the plugin's track, on Jellyfin Web and Jellyfin Android TV. Go if direct play and transcoding both
work on both clients. Otherwise no-go, and Mechanism B starts.

Setup: [dev-server.md](dev-server.md). User `aiko` is mapped to `jpn` by the spike rule. User `bob` is
unmapped. Every result below was heard as well as read: each language is a sine at its own pitch, and
the Web run measures the pitch the player outputs.

## Status

| Client            | Result                                                                                            |
| ----------------- | ------------------------------------------------------------------------------------------------- |
| Jellyfin Web      | **Works**, after the four prototype changes below. As first built in #6, it failed on both the details-page Play button and direct play. |
| Jellyfin Android TV | **To run** by the maintainer on their device: [procedure](#android-tv).                         |

Decision: **pending Android TV**. With these Web results, Mechanism A is a go if Android TV passes the
direct play and transcode cases.

## What the first run showed

The prototype from #6 rewrote the response *after* the server built it. The filter does run on
`POST /Items/{id}/PlaybackInfo`: the server log has its line on every call. Transcoding also follows
the rewritten `TranscodingUrl`. Two things still broke Jellyfin Web:

1. **The details page sends its own `AudioStreamIndex`.** Its Audio menu is pre-selected from the
   item's `MediaSources[].DefaultAudioStreamIndex` (`GET /Items/{id}`,
   `jellyfin-web/src/apps/legacy/controllers/itemDetails/index.js:279`). Play then sends that index
   with the menu's `MediaSourceId` (`index.js:1964`). The filter treated it as the user's own choice
   and stepped aside, so aiko heard English.
2. **The server picks the play method for its own track.** `StreamBuilder` decides between direct play
   and transcoding for the audio stream it was asked for. A browser without `HTMLMediaElement.audioTracks`
   (Chrome, Edge) declares that it can only direct-play a file's first audio track (`IsSecondaryAudio`,
   `jellyfin-web/src/scripts/browserDeviceProfile.js:1004`). When the server's track was the first one,
   the response said direct play, the filter changed `DefaultAudioStreamIndex`, and Chrome played the
   first track anyway. aiko heard English on `S01E02`, while the session reported index 3, Japanese.

Facts from the 12.4 sources that shaped the fix:

- The server ignores a requested `AudioStreamIndex` unless the request also names its `MediaSourceId`
  (`Jellyfin.Api/Helpers/MediaInfoHelper.cs:218-223`).
- Jellyfin Web plays "the played item's own source" whenever the response contains it, whatever the
  order of `MediaSources` (`jellyfin-web/src/components/playback/playbackmanager.js:522-527`).
  Reordering versions in the response has no effect on Web.
- Web's "Remember audio selections" carry-over to the next episode is not purely index-based. It scores
  the previous track's codec, relative index, title and language, and sends the winner as an explicit
  `AudioStreamIndex` (`playbackmanager.js:2508-2600`, `2724-2726`). It follows the language across
  episodes whose track order differs.
- Jellyfin reports the French track of a file tagged `fre` as `fra`. Rules must compare normalised
  codes (SPEC.md "Normalisation").

## Prototype changes (this pull request)

All in `PlaybackInfoFilter`:

1. **Before the action runs**, for a mapped user whose request names no audio stream, the filter
   computes its choice from the item's media sources. It then passes that choice to the action as the
   request's own `mediaSourceId` and `audioStreamIndex`. The server then picks the play method, builds
   `TranscodingUrl` and sets `DefaultAudioStreamIndex` for the plugin's track. Passing the version is
   what makes Web play it. A version the client named is kept.
2. **After the action**, the existing rewrite still runs. It is a no-op for the chosen version and
   corrects the others.
3. **`GET /Items/{id}`** (and `/Users/{userId}/Items/{id}`): the filter sets each version's
   `DefaultAudioStreamIndex` and puts the versions with a match first. The details page then
   pre-selects the plugin's version and track, and its Play button sends them.
4. **An explicit `AudioStreamIndex` in the request** (a manual switch, Web's next-episode carry-over, a
   details-page choice) is still left alone, with a debug line saying so.

## Results: Jellyfin Web

Jellyfin 12.4.0 server (`jellyfin/jellyfin:12.4`) and its bundled Jellyfin Web 12.4.0. Headless
Chromium 154 (no `audioTracks`, plays H.264 / AAC in MKV, like desktop Chrome). Run on Oct 28, 2026 with
`dev/spike-web.cjs`. "Stock" is the same run with the plugin disabled.

| Case                                              | aiko (jpn)                        | bob (unmapped)          | Stock, bob          |
| ------------------------------------------------- | --------------------------------- | ----------------------- | ------------------- |
| Direct play, details-page Play (`Spike Movie`)    | Japanese, DirectPlay, index 1     | English, Transcode, 3   | English, Transcode, 3 |
| Direct play, card play button (`Spike Movie`)     | Japanese, DirectPlay, 1           | English, Transcode, 3   | English, Transcode, 3 |
| Plugin's track not first (`S01E02`, details page) | Japanese, Transcode, 3            | English, DirectPlay, 1  | English, DirectPlay, 1 |
| Same, card play button                            | Japanese, Transcode, 3            | –                       | (aiko stock: English, DirectPlay, 1) |
| Forced transcode, 400 kbps limit (`Spike Movie`)  | Japanese, Transcode, 1            | English, Transcode, 3   | English, Transcode, 3 |
| Forced transcode, 400 kbps limit (`S01E02`)       | Japanese, Transcode, 3            | –                       | –                   |
| Two versions, details page                        | **VO**, Japanese                  | VF, French              | VF, French          |
| Two versions, card play button                    | **VO**, Japanese                  | –                       | –                   |
| Next episode, Remember audio **on**               | Japanese → Japanese (web's carry-over sent index 3) | English → English | English → English |
| Next episode, Remember audio **off**              | Japanese → Japanese (plugin chose 3) | –                    | –                   |
| Switch to English in E01, next, Remember **on**   | English → English (web's carry-over wins) | –               | –                   |
| Switch to English in E01, next, Remember **off**  | English → Japanese (plugin's rule) | –                      | –                   |
| Manual switch during playback                     | English, kept 8 s later (request carried index 2, plugin stepped aside) | – | – |
| aiko and bob on `S01E02` at the same time         | Japanese                          | English                 | –                   |

"Index" is the stream index the server's session reports, video included. The movie's Japanese track is
index 1, so Chrome direct-plays it. `S01E02`'s Japanese track is index 3, not the first, so the server
now transcodes for Chrome, as it does for any non-first track.

Unmapped user: identical to stock in every case, with no plugin log line.

The two-users-at-once case and the API-level cases are also covered by `dev/spike-api.py` (14 checks,
all passing). Among them: `GET /Items/{id}/PlaybackInfo` is rewritten, an explicit index with or
without transcoding is left alone, and the FFmpeg command line of a forced transcode maps the
Japanese stream (`-map 0:0 -map 0:1`).

### Server log

`docker compose logs jellyfin | grep "Smart Audio"` while aiko plays `Spike Versions` from its card on
the home page. First comes the item request, which puts VO first. Then the PlaybackInfo request, which
names no stream:

```text
[17:59:50.645] [DBG] … PlaybackInfoFilter: Smart Audio: user 5fd869b2-…, item 0cffebd2-…, media source d18525a5…: item default audio stream 1 -> 1
[17:59:50.667] [DBG] … PlaybackInfoFilter: Smart Audio: user 5fd869b2-…, item 0cffebd2-…: asking the server for media source d18525a5…, audio stream 1
[17:59:50.673] [DBG] … PlaybackInfoFilter: Smart Audio: user 5fd869b2-…, item 0cffebd2-…, media source d18525a5…: audio stream 1 -> 1
```

`d18525a5…` is the VO file. VF is the item's own version, `0cffebd2…`. From the details page, the
PlaybackInfo line reads `the client asked for its own audio stream, leaving it` instead. The page sends
the version and track it pre-selected from the rewritten item, so they are the plugin's choice already.

On the first build (#6), before the changes above, the log showed
`media source ec32fb38…: audio stream 3 -> 1` for aiko on `Spike Movie`: the filter ran, but only
after the server had chosen English.

### Limits of the Web run

- One browser engine: Chromium, headless. Firefox and Safari have their own `audioTracks` support, so
  their play method can differ. The audio still follows the plugin either way, because the server now
  decides for the plugin's track.
- Every test file uses H.264 / AAC. Codecs the browser can't play transcode anyway, and that path works.
- When the plugin picks a track that isn't the file's first, Chrome now transcodes the audio where stock
  Jellyfin might have direct-played the file's first track. That is stock behaviour for a non-first
  track (the same happens when a user picks it), not something the plugin adds.

## Android TV

To run on the maintainer's device, against the dev server (`http://<lan-ip>:8096`). The Android TV
client's source is not in the pinned references, so nothing here is predicted.

1. Start the server as in [dev-server.md](dev-server.md), with this branch built.
2. Keep a terminal on `docker compose logs -f jellyfin | grep "Smart Audio"`.
3. Sign in as `aiko`, run each case, and note what you hear (Japanese 330 Hz high, French 262 Hz middle,
   English 196 Hz low). Also note what Dashboard → Activity (or the session's "Playback info" panel)
   says about the play method, and whether a "Smart Audio" line appeared:
   - `asking the server for …`: the client let the plugin choose;
   - `the client asked for its own audio stream`: the client sent an index, so the plugin stepped aside.
     Expected after a manual switch. Fine when the client read that index from the rewritten item
     (`item default audio stream … -> …` just before), as Web's details page does;
   - no line: the client didn't call `POST /Items/{id}/PlaybackInfo`.
4. Repeat the stock cases as `bob`.
5. Paste the filled table, the app version (Settings → About) and any surprising log lines in the pull
   request or issue #7.

| Case                                              | Expected (aiko)           | Heard | Play method | Log line | Notes |
| ------------------------------------------------- | ------------------------- | ----- | ----------- | -------- | ----- |
| `Spike Movie`, Play from its details page         | Japanese                  |       |             |          |       |
| `Spike Movie`, play from a home row               | Japanese                  |       |             |          |       |
| `Spike Show - S01E02` (Japanese is the 3rd track) | Japanese                  |       |             |          |       |
| Forced transcode: Settings → Playback → max bitrate 0.5 Mbps, `Spike Movie` | Japanese |  |       |          |       |
| Same, `S01E02`                                    | Japanese                  |       |             |          |       |
| `Spike Versions`                                  | VO, Japanese              |       |             |          |       |
| Next episode from S01E01, Remember audio on       | Japanese on E02           |       |             |          |       |
| Same, Remember audio off                          | Japanese on E02           |       |             |          |       |
| Switch to English during S01E01, wait 30 s        | stays English             |       |             |          |       |
| Then next episode                                 | (record it)               |       |             |          |       |
| bob, `Spike Movie`                                | English (stock)           |       |             |          |       |
| aiko on the TV and bob on Web, `S01E02`, same time | Japanese / English       |       |             |          |       |

"Remember audio selections" is a per-user setting (web: Settings → Playback; or
`node dev/spike-web.cjs aiko "Spike Show - S01E01" remember=off`, which sets it before playing).

## Decision

Pending Android TV:

- **Go** if Android TV starts on the plugin's track in direct play and forced transcode. Mechanism A
  stays, with the changes above written into SPEC.md, and #8 (Mechanism B) is closed as not planned.
- **No-go** if Android TV ignores both the response and the requested stream. Mechanism B (#8) starts.
  It may be needed for that client only, next to A for Web.
