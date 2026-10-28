# Dev server

A Jellyfin 12.4 server in Docker with the plugin loaded, test media and test users, for trying the
plugin by hand and for the spike in [spike-mechanism-a.md](spike-mechanism-a.md).

Needs Docker with Compose, the .NET 10 SDK and Python 3. Everything runs from the repository root.

## Start it

```sh
dotnet build -c Release            # the plugin, in src/Jellyfin.Plugin.SmartAudio/bin/Release/net10.0
dev/make-test-media.sh             # test media in dev/media, with the ffmpeg of the Jellyfin image
docker compose up -d               # jellyfin/jellyfin:12.4 on http://localhost:8096
python3 dev/setup-server.py        # wizard, libraries, users, the plugin's spike rule
```

Then open <http://localhost:8096>. From another device (a TV, a phone) use the machine's LAN address,
`http://<lan-ip>:8096`.

| User    | Password | Spike rule                                  |
| ------- | -------- | ------------------------------------------- |
| `admin` | `admin`  | none (administrator)                        |
| `aiko`  | `aiko`   | `jpn`: starts on the Japanese track         |
| `bob`   | `bob`    | none: behaves like stock Jellyfin           |

Test-only passwords: don't expose this server to the internet. The spike rule is edited in Dashboard →
Plugins → Smart Audio Language.

## Test media

`dev/make-test-media.sh` writes 60-second H.264 files with AAC stereo tracks. Each language is a sine
at its own pitch, so you can tell by ear which track plays: **Japanese 330 Hz** (high E), **French
262 Hz** (middle C), **English 196 Hz** (low G).

| File                                            | Audio tracks, in file order                   | For                                   |
| ----------------------------------------------- | --------------------------------------------- | ------------------------------------- |
| `Movies/Spike Movie (2026)/Spike Movie (2026).mkv` | jpn, fre, eng (default)                    | direct play, transcode                |
| `Movies/Spike Versions (2026)/… - VF.mkv`       | fre (default), eng                            | two versions of one movie             |
| `Movies/Spike Versions (2026)/… - VO.mkv`       | jpn (default), fre                            | two versions of one movie             |
| `Shows/Spike Show/Season 01/… - S01E01.mkv`     | jpn, eng (default), fre                       | next episode, track order changes     |
| `Shows/Spike Show/Season 01/… - S01E02.mkv`     | eng (default), fre, jpn                       | next episode, track order changes     |

Stream indexes count the video stream: in `Spike Movie`, Japanese is index 1 and English index 3.

## Day to day

- **After a rebuild:** `dotnet build -c Release && docker compose restart jellyfin`. The build output
  folder is mounted as the plugin's folder, writable: the server writes the plugin's `meta.json` there
  and doesn't start if it can't.
- **What the plugin did:** its log lines are at Debug level, switched on for the plugin only by the
  compose file. `docker compose logs -f jellyfin | grep "Smart Audio"`:

  ```text
  … asking the server for media source ec32fb38…, audio stream 1
  … media source ec32fb38…: audio stream 1 -> 1
  ```

- **What the server did:** Dashboard → Activity and Dashboard → Logs. Each transcode has its own
  `FFmpeg.Transcode-*.log`; the `-map 0:<n>` after the video's says which audio stream it encodes.
- **Server-side checks:** `python3 dev/spike-api.py` calls PlaybackInfo as the test users, starts a
  transcode and reads its FFmpeg log back; it prints one PASS / FAIL line per check.
- **Jellyfin Web, automated:** `node dev/spike-web.cjs aiko "Spike Movie (2026)"` plays an item in
  headless Chromium and reports the pitch it hears (needs `npm install playwright`). See the comment at
  the top of the script for the options.
- **Start over:** `docker compose down -v` deletes the server's configuration and database (not the
  media), then the four commands above again.
