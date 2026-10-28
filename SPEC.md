# Jellyfin Smart Audio Language Plugin – Spec

Oct 28, 2026 · @Bryan Smée

## Overview

This plugin makes Jellyfin pick the right audio track automatically, per user. Each user chooses between a fixed language and the title's original language, can override that per library, series or movie, and gets a predictable fallback chain when the preferred track is missing.

**Problem today.** Jellyfin stores one preferred audio language per user (`AudioLanguagePreference`) plus a "play default track" flag. There is no "original language" mode, no ordered fallback list, no per-series rule, and no notion of regional variants (VFF vs VFQ, Castilian vs Latin American Spanish).

**Goals**

- Per-user mode: *Fixed language* or *Original version*.
- Ordered fallback chain per user (e.g. original → French → English → file default).
- Per-user overrides at library, series/season and movie level.
- Original language resolved from external metadata providers, cached, manually overridable.
- Regional variant awareness (French, Spanish, Portuguese, Chinese) with per-user variant preference.
- Works without remuxing or modifying media files.

**Non-goals (v1)**

- Changing files on disk (default flags, track order).
- Transcoding or audio processing.
- Language detection from the audio signal itself (possible later, see open questions).
- Subtitle rules: specified as phase 2, below.

**Target.** Current stable Jellyfin server (12.4.x at time of writing; dev to confirm the exact ABI version) and .NET version it requires.

## User preferences model

Every setting is stored per Jellyfin user ID. A rule is resolved from the most specific scope down to the user's global default.

**Global profile (per user)**

| Field | Type | Example | Notes |
| --- | --- | --- | --- |
| `Enabled` | bool | true | Off = native Jellyfin behaviour for this user |
| `Mode` | enum `Fixed` / `Original` | Original | Primary intent |
| `FixedLanguage` | language tag | `fr-FR` | Used when Mode = Fixed |
| `FallbackChain` | ordered list of tags | `[fr-FR, en, *original*, *default*]` | Tried in order when the primary target has no track |
| `VariantPreferences` | map base → ordered variants | `fr: [fr-FR, fr-CA]`, `es: [es-419, es-ES]` | Which variant wins when several exist |
| `StrictVariant` | bool | false | true = never accept the other variant (VFQ is not a valid substitute for VFF) |
| `AvoidCommentary` | bool | true | Skip commentary tracks unless nothing else matches |
| `AvoidAudioDescription` | bool | true | Skip descriptive audio unless explicitly requested |
| `QualityTiebreak` | enum | `MostChannels` | Tie-break between equal-language tracks: most channels, best codec, file order |
| `RespectManualChoice` | bool | true | A manual switch during playback silently becomes a series override, with a notification where the client supports it (see below) |

*Special tokens in the chain:* `*original*` = the title's resolved original language, `*default*` = the track flagged default in the file, `*first*` = first audio track.

**Overrides (per user)**

| Scope | Key | Can set |
| --- | --- | --- |
| Library | library ID | Mode, FixedLanguage, FallbackChain |
| Series | series ID | Mode, FixedLanguage, FallbackChain, pinned variant |
| Season | season ID | Same as series (rare: seasons dubbed differently) |
| Movie / episode | item ID | Same, plus a pinned track index (last resort for broken tags) |

**Resolution order:** item → season → series → library → global profile → server default profile. The first scope that defines a field wins for that field; fields not defined fall through. Example: library "Anime" sets Mode = Original, the user's global FallbackChain still applies.

**Server default profile (admin).** The admin defines a default profile (same fields as the global profile) on the dashboard page. It applies to every user who hasn't set a field themselves, including users created later, and can also carry default library overrides (e.g. "Anime" library → Original). A user's own settings always take precedence; the admin can reset a user to defaults.

**Manual switches.** When `RespectManualChoice` is on and the user changes audio track during playback of an episode, the plugin silently stores a series-level override with that track's language + variant. It then sends a notification to the playing session ("Audio for *Series* set to Japanese — change in Audio language settings") through Jellyfin's display-message session command. Clients that don't show session messages simply get no notification; the override still applies, and it is visible and removable on the user page. This replaces Jellyfin's index-based "remember selections", which breaks when track order differs between episodes.

## Original language resolution

The original language is resolved once per movie or series, from the provider IDs Jellyfin already stores on the item, then cached. Episodes and seasons inherit from their series.

**Bundled mapping dataset (no API key needed)**

The default source is a static mapping `provider ID → original language`, versioned in the plugin's repository. Most installs never call an external API. Original language practically never changes, so entries are written once and kept.

- **Format:** one file per provider and type, sorted, tab-separated, gzipped. One line per title: `603\tja`. Files: `tmdb-movie.tsv.gz`, `tmdb-tv.tsv.gz`, `tvdb-series.tsv.gz`, `imdb.tsv.gz`, `anilist.tsv.gz`. Plus a `corrections.tsv` that always wins (fixes from issues and PRs).
- **Delivery:** published as release assets with a version and checksum, separate from the plugin DLL, so the data updates without a plugin release. The plugin downloads it on install and checks weekly; it also works offline with the last copy.
- **Generation:** a script run by a scheduled GitHub workflow (weekly). It lists candidate IDs, **skips IDs already in the file**, looks up only new ones, appends them, sorts, and opens a PR (or commits directly). Entries are never re-fetched; changes go through `corrections.tsv` only.
- **Candidate IDs:** the TMDB daily ID export files for movies and series (all IDs, refreshed daily), so coverage grows over time without guessing what users own. Optional priority by popularity so the first runs cover what most people have.
- **Source for values:** preferably **Wikidata** (CC0, no key, no redistribution issue): "original language of film or TV show" plus its TMDB, IMDb, TVDB and AniList ID properties, fetched in bulk via SPARQL. Gaps filled from the TMDB API with the workflow's own key, subject to the licence question below. Dev to confirm the Wikidata property IDs.
- **Size:** roughly 1 million movies plus a few hundred thousand series; at about 10 bytes per gzipped line that is on the order of 10 MB in total (estimate; measure on the first run).
- **Lookup order in the plugin:** manual override → `corrections.tsv` → bundled dataset → local cache of live lookups → live API (only if the admin entered a key) → heuristic.
- **Missing entries:** logged on the admin page with a "report missing" link to a prefilled GitHub issue. No automatic reporting.

**Live provider priority (only when the bundled dataset has no entry; configurable by the admin)**

| # | Provider | Lookup key | Field used | Notes |
| --- | --- | --- | --- | --- |
| 1 | Manual override | item ID | admin- or user-set value | Always wins |
| 2 | TMDB | `Tmdb` provider ID (or IMDb via TMDB `/find`) | `original_language` (ISO 639-1), plus `origin_country`, `spoken_languages` | Best coverage for movies and series. API key in plugin config |
| 3 | TVDB | `Tvdb` provider ID | series `originalLanguage` (ISO 639-2), `originalCountry` | Good for series; v4 API needs a key |
| 4 | AniList / AniDB | `AniList` / `AniDB` provider ID if present | country of origin (JP, CN, KR) mapped to a language | Anime safety net; no language field, map country → language |
| 5 | Heuristic | file tracks | language of the track flagged default when only 2 tracks exist and one matches the library's "local" language | Lowest confidence, flagged as guessed |

Each result is stored with `source`, `confidence` (high / medium / guessed) and `fetchedAt`. Refresh on metadata refresh, or every 90 days.

**Normalisation.** Store everything as BCP 47 (`ja`, `fr`, `zh-yue`). Map ISO 639-1 ↔ 639-2/B ↔ 639-2/T (`fre`/`fra`, `ger`/`deu`, `chi`/`zho`). Map TMDB's non-standard `cn` to Cantonese (`yue`), and its `xx` (no language) to "none".

**Edge cases**

- **Providers disagree** (e.g. TMDB `en`, TVDB `jpn`): take the higher-priority provider, log the conflict, show it in the admin UI for review.
- **Co-productions** with several spoken languages: use `original_language` only, never `spoken_languages[0]`.
- **Chinese:** TMDB `zh` does not say Mandarin vs Cantonese. Use origin country (HK → Cantonese, CN/TW → Mandarin) as a hint; allow manual override.
- **Silent films / no dialogue** (`xx`): skip `*original*` and continue the chain.
- **Original language not in the file** (French user, file has only FR + EN, film is Korean): `*original*` fails, chain continues. This is expected, not an error.
- **Post-synced originals** (old Italian films shot without sound): original stays `it`; no special handling.
- **Missing provider IDs:** item is marked "unresolved"; the chain skips `*original*` and the admin UI lists such items.
- **Rate limits / offline:** never resolve at playback time. Resolve in a scheduled task and on item-added events; playback only reads the cache.

## Track matching and language variants

Each audio track is classified into a normalised **(language, variant, kind)** tuple before any rule runs. The 3-letter tag alone is not enough: `fre` cannot tell VFF from VFQ, `spa` cannot tell Castilian from Latino.

**Inputs per track (from Jellyfin's `MediaStream`)**: language tag, title, display title, codec, channels, `IsDefault`, `IsForced`, index, external file name for external audio. If the dev can read the Matroska BCP 47 tag (`LanguageIETF`, e.g. `fr-CA`) through ffprobe output, it takes priority over title parsing.

**Variant detection (title / filename regex, case-insensitive, configurable by admin)**

| Base | Variant | Tag | Title patterns |
| --- | --- | --- | --- |
| French | France (VFF) | `fr-FR` | `VFF`, `TrueFrench`, `VF France`, `French (France)` |
| French | Québec (VFQ) | `fr-CA` | `VFQ`, `Québec`, `Quebecois`, `Canadien`, `French (Canada)`, `VFC` |
| French | Generic / unknown | `fr` | `VF`, `VF2`, `VFI`, `French` with no qualifier |
| Spanish | Spain (Castilian) | `es-ES` | `Castellano`, `Castilian`, `España`, `Spain` |
| Spanish | Latin America | `es-419` | `Latino`, `LatAm`, `Latinoamérica`, `Mexico`, `es-MX` |
| Portuguese | Brazil | `pt-BR` | `Brasil`, `Brazilian`, `PT-BR` |
| Portuguese | Portugal | `pt-PT` | `Portugal`, `European`, `PT-PT` |
| Chinese | Mandarin | `zh-cmn` | `Mandarin`, `国语`, `普通话`, tag `cmn` |
| Chinese | Cantonese | `zh-yue` | `Cantonese`, `粤语`, tag `yue` |
| English | (no split by default) | `en` | admin may add `en-GB` / `en-US` if needed |

Note: "VF2" and "VFI" are ambiguous in practice (often international French = France French). Default mapping is generic `fr`; admin can remap.

**Track kind detection**

- **Commentary:** title contains `commentary`, `commentaire`, `comentario`, `director`.
- **Audio description:** title contains `AD`, `audio description`, `audiodescription`, `descriptive`, `VI`, `visually impaired`, or the Matroska "visual impaired" flag if exposed.
- **Karaoke / music only / isolated score:** `karaoke`, `isolated score`, `music only`.
- Everything else is `main`.

**Bad or missing tags**

- `und` / empty tag: try title parsing (`Japanese`, `日本語`, `Français`…). Still unknown → language = unknown, only matches `*default*` / `*first*`.
- **Mis-tagged tracks** (two tracks both tagged `eng`, one actually Russian): cannot be fixed automatically; the item-level pinned track index is the escape hatch.
- **Several tracks, same language and variant** (5.1 + stereo, or TrueHD + AC3): apply `QualityTiebreak`; never pick commentary over main.
- **Multiple versions of a movie** (separate files merged as versions, e.g. `Movie - VF.mkv` and `Movie - VO.mkv`): the rule chooses the **media source** as well as the track, scoring each source's best track.
- **External audio files** (`Movie.fr.mka`): language and variant read from the file name suffix, same patterns.

## Selection algorithm

The algorithm builds one ordered list of targets from the effective rule, then returns the first target that matches a usable track. It is pure and deterministic, so it can be unit-tested without a server.

```
function SelectAudio(user, item, mediaSources):
    rule      = ResolveRule(user, item)          // item → season → series → library → global
    if rule.pinnedTrackIndex: return that track
    original  = OriginalLanguageCache[item.seriesOrMovieId]   // may be null

    targets = []
    if rule.mode == Original: targets += original
    else:                     targets += rule.fixedLanguage
    targets += rule.fallbackChain           // expand *original*, *default*, *first*
    targets  = dedupe(targets, skipNull)

    for source in OrderSources(mediaSources):          // versions of the same item
        tracks = Classify(source.audioStreams)         // (lang, variant, kind, quality)
        usable = tracks.where(kind == main)            // commentary / AD excluded per prefs
        for target in targets:
            candidates = Match(usable, target, rule.variantPrefs, rule.strictVariant)
            if candidates: return (source, Best(candidates, rule.qualityTiebreak))
    // nothing matched, including *default*: return null → Jellyfin native behaviour
    return null

function Match(tracks, target, variantPrefs, strict):
    exact   = tracks where lang+variant == target
    if exact: return exact
    if target has no variant:               // target = "fr"
        for v in variantPrefs[target.base]: // [fr-FR, fr-CA]
            hit = tracks where lang+variant == v
            if hit: return hit
        return tracks where base == target.base   // unknown variant
    if not strict:
        return tracks where base == target.base and variant is unknown
    return []
```

In non-strict mode, a target `fr-FR` accepts a track tagged plain `fr` (unknown variant), but never an explicit `fr-CA`. This avoids silently playing VFQ to a VFF user while still handling the common untagged case.

**Worked examples**

| User rule | File tracks | Original | Result | Why |
| --- | --- | --- | --- | --- |
| Original, chain `[fr-FR, en]` | jpn, fre (VFF), eng | ja | Japanese | Original present |
| Original, chain `[fr-FR, en]` | fre (VFF), eng | ko | French VFF | Original absent, chain step 1 |
| Fixed `fr-FR`, strict | fre "VFQ", eng | en | English | VFQ refused by strict; chain falls to en |
| Fixed `fr-FR`, not strict | fre "VFQ", fre "VFF", eng | en | French VFF | Exact variant wins over other variant |
| Fixed `fr-FR`, not strict | fre "VF" (no qualifier), eng | en | French (generic) | Unknown variant accepted |
| Fixed `es`, prefs `[es-419, es-ES]` | spa "Castellano", spa "Latino" | en | Spanish Latino | Variant preference order |
| Original | jpn 2.0, jpn 5.1, jpn commentary | ja | Japanese 5.1 | Tiebreak MostChannels, commentary skipped |
| Original, series override Fixed `en` | jpn, eng | ja | English | Series override beats global mode |
| Original | und, und | ja (unresolved tags) | file default | Nothing matched, `*default*` in chain |

## Technical architecture

The plugin decides server-side and hands the choice to clients through fields they already honour, so no client needs changes. Two delivery mechanisms are specified; the dev must validate both against the target server version before committing (first milestone).

**Mechanism A: rewrite the PlaybackInfo response (preferred)**

Clients call `POST /Items/{id}/PlaybackInfo` before playing. The server returns `MediaSources[]`, each with `DefaultAudioStreamIndex`, which official clients use as the initial track. The plugin registers an ASP.NET Core result filter (through `IPluginServiceRegistrator` and `MvcOptions.Filters`) that post-processes this response for the calling user: sets `DefaultAudioStreamIndex`, and reorders `MediaSources` when another version scores better. To verify: that filters registered by a plugin run on this controller, and that transcoding sessions pick up the rewritten index.

**Mechanism B: pre-seed per-user item data (fallback)**

Jellyfin stores `AudioStreamIndex` in each user's item data and reuses it when "Remember audio selections" is on. The plugin writes the computed index for every item per user, in a scheduled task and on item-added events, and forces that user setting on. Simpler and upgrade-safe, but index-based: it must be recomputed after every library rescan, and it overwrites the user's own remembered choice.

**Other components**

- **Original-language resolver:** scheduled task + `ILibraryManager.ItemAdded` / `ItemUpdated` handlers; HTTP clients for TMDB, TVDB, AniList with retry, rate limiting and a local cache.
- **Track classifier:** pure function over `MediaStream` lists; shared by the filter, the pre-seed task and the admin preview.
- **Manual-switch listener:** subscribes to `ISessionManager` playback progress events; when the playing `AudioStreamIndex` differs from the plugin's choice for more than \~30 s, records a series-level override for that user and sends a session notification (best effort, client-dependent).
- **Storage:** plugin data folder, SQLite (preferred) or JSON. Tables: `UserProfile`, `UserOverride(userId, scope, scopeId, …)`, `OriginalLanguage(itemId, lang, source, confidence, fetchedAt)`, `VariantPatterns`.

**Client compatibility (to verify with a test matrix)**

| Client | Expected to honour server default | Notes |
| --- | --- | --- |
| Jellyfin Web, Jellyfin Desktop / Media Player | Likely | Baseline for development |
| Jellyfin Android (ExoPlayer / web wrapper) | Likely | Test both player engines |
| Jellyfin Android TV | Uncertain | Known to handle language memory differently; test early |
| Swiftfin, Findroid, Streamyfin | Uncertain | Third-party; document results |
| Kodi add-ons, Infuse | Uncertain | May select tracks on their own; document as limitation |

**User interface**

- **Admin dashboard page** (standard plugin config page): provider API keys and priority, variant regex editor, per-user profiles, list of unresolved or conflicting original languages with manual override.
- **User-facing page:** Jellyfin has no plugin extension point in the user's own settings. v1 serves a small authenticated page from the plugin (`/SmartAudio/ui`, uses the user's existing session token) where each user edits their profile and overrides. Optional v2: inject an "Audio language…" entry into the series/movie context menu of the web client via a script-injection plugin.
- **Item preview:** on the admin page and the user page, a "What would play?" panel showing the resolved rule, original language, classified tracks and the chosen one. Essential for debugging tags.

## Subtitles (phase 2)

Subtitles follow the chosen audio, so they are specified now and built after audio is stable. Same profile, overrides and variant classifier.

- **Audio understood** (audio language is in the user's "understood" list): no subtitles, except *forced* subtitles in the user's language.
- **Audio not understood:** full subtitles, first match in a subtitle chain (e.g. `[fr-FR, en]`), never *forced*-only tracks.
- **Avoid mismatched subs:** never pick a subtitle track whose title marks it as a transcript of a dub (`dubtitles`, `CC for dub`) unless the dub is the playing audio.
- **Prefer** full over SDH by default (user option), text over image-based when both exist.

## API

All endpoints authenticated with the normal Jellyfin token; a user can only read and write their own data, admins any user.

| Method | Path | Purpose |
| --- | --- | --- |
| GET / PUT | `/SmartAudio/Users/{userId}/Profile` | Global profile |
| GET | `/SmartAudio/Users/{userId}/Overrides` | List overrides |
| PUT / DELETE | `/SmartAudio/Users/{userId}/Overrides/{scope}/{scopeId}` | Set or clear an override |
| GET | `/SmartAudio/Items/{itemId}/Preview?userId=` | Resolved rule, tracks, chosen track |
| GET / PUT | `/SmartAudio/Items/{itemId}/OriginalLanguage` | Read or manually set (admin) |
| POST | `/SmartAudio/Tasks/Resolve` | Trigger the resolver (admin) |

## Acceptance tests

- [ ] Every row of the worked-examples table passes as a unit test of `SelectAudio`.
- [ ] Variant regex table: each pattern classified correctly, plus 20 real-world titles collected from the admin's library.
- [ ] Two users, same episode, different profiles, played in parallel: each gets their own track.
- [ ] Series override set from the user page applies to every episode, including newly added ones.
- [ ] Manual switch during episode 1 is applied automatically to episode 2 on Web and Android TV.
- [ ] Episode with track order different from the previous one still gets the right language (index-based memory would fail).
- [ ] Movie with two versions (VF file and VO file) starts the right version.
- [ ] TMDB unreachable: playback still works from cache, no added latency above 50 ms on PlaybackInfo.
- [ ] Plugin disabled for a user: behaviour identical to stock Jellyfin.
- [ ] Client compatibility matrix filled in and published in the README.

## Distribution

The plugin is published openly and installed through a public plugin repository, so it must work for strangers' setups, not only ours.

- **Source:** public GitHub repository under a GPL-compatible licence (Jellyfin is GPL-2.0; follow the official plugin template).
- **Install:** a plugin repository `manifest.json` hosted with the releases, so admins add one repository URL in the dashboard. Builds, checksums and manifest updated by CI on each tagged release.
- **Versioning:** each release declares its target ABI (server version); keep a compatibility table in the README.
- **No bundled secrets:** API keys are optional. The bundled dataset covers lookups; admins who want live lookups for titles missing from it enter their own TMDB / TVDB keys. If the bundled TMDB metadata plugin's key can be reused safely, offer it as an option; otherwise document how to get a free key. Show the TMDB attribution its terms require.
- **Safe defaults:** plugin installed = no behaviour change until the admin enables it or sets a server default profile.
- **Localisation:** UI strings in resource files (English + French at launch); variant patterns editable so other communities can add theirs.
- **Docs:** README with setup, the client compatibility matrix, and a troubleshooting guide based on the preview panel.
- **Telemetry:** none.

## Milestones

1. **Spike (go / no-go):** prove Mechanism A on the target server and on Web + Android TV. If A fails, switch to B.
2. **Core:** classifier, selection algorithm, global profile, original-language resolver (bundled dataset + generation workflow, TMDB live lookup), admin page.
3. **Overrides:** library / series / movie overrides, user page, preview panel.
4. **Hardening:** TVDB + AniList, conflict review, manual-switch listener, compatibility matrix.
5. **Phase 2:** subtitles.

## Decisions and open questions

**Decided**

- **Manual switch:** creates a series override silently, with a notification on clients that support session messages.
- **Admin defaults:** the admin sets a server default profile for all users; users override it.
- **Distribution:** public plugin repository.

**Still open**

- **Dataset licence:** TMDB's API terms may not allow republishing data pulled from its API in a public repository. Check the terms before using TMDB as a source for the dataset; if not allowed, build it from Wikidata only and keep TMDB for opt-in live lookups.
- Optional later: detect the spoken language of `und` tracks with a speech language-ID model, run offline as a scheduled task?
