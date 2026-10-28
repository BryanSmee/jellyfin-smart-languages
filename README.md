# jellyfin-smart-languages

Jellyfin plugin that picks each user's audio track automatically: fixed language or original
version, ordered fallback chains, per-library and per-series overrides, and regional variants
(VFF / VFQ, Castilian / Latin American Spanish). See [SPEC.md](SPEC.md).

Targets Jellyfin 12.4 (`net10.0`). Work in progress: the plugin loads but does nothing yet.

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet build -c Release
dotnet test
dotnet format --verify-no-changes
```

The plugin is `src/Jellyfin.Plugin.SmartAudio/bin/Release/net10.0/Jellyfin.Plugin.SmartAudio.dll`.
Every CI run also uploads it as the `Jellyfin.Plugin.SmartAudio` artifact (Actions tab → the run →
Artifacts).

## Dev server

A Jellyfin 12.4 server in Docker with the plugin, test media and test users, in four commands:
[docs/dev-server.md](docs/dev-server.md).

## Install by hand

1. Create a folder for the plugin in the server's `plugins/` folder, which is in its data
   directory: `/var/lib/jellyfin/plugins/` on Debian/Ubuntu packages, `/config/plugins/` in the
   official Docker image, `%LOCALAPPDATA%\jellyfin\plugins\` on Windows. For example
   `plugins/Smart Audio Language/`.
2. Copy `Jellyfin.Plugin.SmartAudio.dll` into it.
3. Restart Jellyfin. The plugin shows up in Dashboard → Plugins → My plugins.

## Licence

GPL-3.0, see [LICENSE](LICENSE).
