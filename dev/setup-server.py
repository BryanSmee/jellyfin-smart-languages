#!/usr/bin/env python3
"""Sets up a fresh dev server for the spike (docs/spike-mechanism-a.md), through the API only.

Runs the startup wizard, adds the test libraries, creates the test users and sets the plugin's
spike rule. Test-only credentials: never point it at a real server.

    python3 dev/setup-server.py [http://localhost:8096]
"""

import json
import sys
import time
import urllib.error
import urllib.request

SERVER = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8096").rstrip("/")
PLUGIN_ID = "26618d45-9cdd-4fda-8779-5baa1ec293b1"

# name, password, spike rule language (None: unmapped user, behaves like stock Jellyfin)
ADMIN = ("admin", "admin")
USERS = [("aiko", "aiko", "jpn"), ("bob", "bob", None)]
LIBRARIES = [("Movies", "movies", "/media/Movies"), ("Shows", "tvshows", "/media/Shows")]

# No online metadata: titles stay the file names ("Spike Show" would otherwise match a real series).
OFFLINE = {"TypeOptions": [
    {"Type": kind, "MetadataFetchers": [], "MetadataFetcherOrder": [], "ImageFetchers": [], "ImageFetcherOrder": []}
    for kind in ("Movie", "Series", "Season", "Episode")
]}


def call(method, path, body=None, token=None, query=""):
    auth = 'MediaBrowser Client="spike-setup", Device="script", DeviceId="spike-setup", Version="1"'
    if token:
        auth += f', Token="{token}"'
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(f"{SERVER}{path}{query}", data=data, method=method)
    request.add_header("Authorization", auth)
    if data is not None:
        request.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(request) as response:
        text = response.read()
        return json.loads(text) if text else None


def login(name, password):
    return call("POST", "/Users/AuthenticateByName", {"Username": name, "Pw": password})


def wait_for_server():
    for _ in range(60):
        try:
            info = call("GET", "/System/Info/Public")
            # While starting up, the server answers without its version.
            if info and "Version" in info:
                return info
        except (urllib.error.URLError, ConnectionError, ValueError):
            pass
        time.sleep(2)
    sys.exit(f"No Jellyfin server answering at {SERVER}")


def main():
    info = wait_for_server()
    print(f"Jellyfin {info['Version']} at {SERVER}")

    if not info["StartupWizardCompleted"]:
        call("POST", "/Startup/Configuration", {"UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
        call("GET", "/Startup/User")
        call("POST", "/Startup/User", {"Name": ADMIN[0], "Password": ADMIN[1]})
        call("POST", "/Startup/RemoteAccess", {"EnableRemoteAccess": True})
        call("POST", "/Startup/Complete")
        print(f"Startup wizard done, admin user {ADMIN[0]} / {ADMIN[1]}")

    token = login(*ADMIN)["AccessToken"]

    existing = {folder["Name"] for folder in call("GET", "/Library/VirtualFolders", token=token)}
    for name, kind, path in LIBRARIES:
        if name not in existing:
            call("POST", "/Library/VirtualFolders", {"LibraryOptions": OFFLINE}, token,
                 f"?name={name}&collectionType={kind}&paths={path}")
            print(f"Library {name} added ({path})")
    # One scan once both libraries exist: a scan per added library can miss the second one.
    call("POST", "/Library/Refresh", token=token)

    users = {user["Name"]: user["Id"] for user in call("GET", "/Users", token=token)}
    rule = []
    for name, password, language in USERS:
        if name not in users:
            users[name] = call("POST", "/Users/New", {"Name": name, "Password": password}, token)["Id"]
            print(f"User {name} / {password} created")
        if language:
            rule.append({"UserId": users[name], "Language": language})

    config = call("GET", f"/Plugins/{PLUGIN_ID}/Configuration", token=token)
    config["SpikeUserLanguages"] = rule
    call("POST", f"/Plugins/{PLUGIN_ID}/Configuration", config, token)
    print("Spike rule: " + ", ".join(f"{name} -> {language}" for name, _, language in USERS if language))

    print("Waiting for the library scan...")
    for _ in range(90):
        items = call("GET", "/Items", token=token, query="?Recursive=true&IncludeItemTypes=Movie,Episode")
        if items["TotalRecordCount"] >= 4:
            break
        time.sleep(2)
    print(f"{items['TotalRecordCount']} movies and episodes in the library. Done: open {SERVER}")


if __name__ == "__main__":
    main()
