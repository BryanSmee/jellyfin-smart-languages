# Jellyfin Smart Audio Language plugin

- `SPEC.md` is the source of truth: behaviour, data model, API, acceptance tests and milestones.
  Say in the pull request when a change departs from it.
- Target: Jellyfin 12.4 (`net10.0`). Laid out like jellyfin/jellyfin-plugin-template, but with
  `Jellyfin.Controller` / `Jellyfin.Model` pinned to `12.4.0` from NuGet: the template still
  targets 10.11.
- Pinned v12.4 sources, read-only, the reference for every API question. Don't fetch other
  versions from GitHub.
  - `/opt/jellyfin`: the server (`MediaInfoController`'s PlaybackInfo,
    `IPluginServiceRegistrator`, `ISessionManager`, `ILibraryManager`, `MediaStream`).
  - `/opt/jellyfin-web`: the web client (how it reads `DefaultAudioStreamIndex`, session
    messages).
- Keep the track classifier and `SelectAudio` pure, with no server dependencies, so unit tests
  cover them. Each row of the spec's worked-examples table is a test.
- Checks before pushing, once a solution exists:
  - `dotnet build -warnaserror`
  - `dotnet test`
  - `dotnet format --verify-no-changes`
- No running Jellyfin server in the sandbox: say which behaviour against a live server
  (Mechanism A, clients) wasn't verified.
- No secrets in the repo: TMDB/TVDB keys come from plugin configuration or workflow secrets.
