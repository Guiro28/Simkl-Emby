# Simkl for Emby — “Trakt parity” edition

[🇫🇷 Français](README.md) · 🇬🇧 English

[![license](https://img.shields.io/github/license/Guiro28/Simkl-Emby.svg?style=flat-square)][license]

A **Simkl** tracking plugin for **Emby**. This is a fork of the official
[SIMKL/Emby](https://github.com/SIMKL/Emby) plugin, extended to provide **the same
features as the [Trakt for Emby](https://github.com/MediaBrowser/trakt) plugin**.

The original plugin only marked an item “watched” once a given percentage was
reached. This version does full **two-way synchronization** between Emby and Simkl.

---

## Table of contents
- [Features](#features)
- [Installation](#installation)
- [Configuration](#configuration)
- [Scheduled tasks](#scheduled-tasks)
- [How it works](#how-it-works)
- [Design decisions](#design-decisions)
- [Building from source](#building-from-source)
- [Architecture](#architecture)
- [Credits](#credits)

---

## Features

| Feature | Original plugin | This fork |
|---------|:--------------:|:--------:|
| Real-time scrobbling **start / pause / stop** (“watching” status) | ❌ | ✅ |
| **Resume points**: paused Simkl sessions → Emby playback position | ❌ | ✅ |
| Manually mark **watched / unwatched** → pushed to Simkl | ❌ | ✅ |
| Scheduled task **Sync library to Simkl** (Emby → Simkl) | ❌ | ✅ |
| Scheduled task **Import playstates from Simkl** (Simkl → Emby) | ❌ | ✅ |
| **Ratings** sync (movies & shows) | ❌ | ✅ |
| Add unwatched library items to **“Plan to watch”** | ❌ | ✅ |
| **Multi-user** + per-user **excluded folders** | partial | ✅ |
| **PIN** login (no password) | ✅ | ✅ |
| **Filename** fallback when the id is missing | ✅ | ✅ (kept) |

Compatible with **Emby 4.7+** (tested on 4.10). The Simkl API key is bundled in
the plugin: **no developer application to create**.

---

## Installation

### From the release (recommended)
1. Download `Simkl.dll` from the [latest release](https://github.com/Guiro28/Simkl-Emby/releases).
2. Copy it into the Emby plugins folder:
   - **Windows**: `%AppData%\Emby-Server\programdata\plugins\`
   - **Linux**: `/var/lib/emby/plugins/`
   - **Docker / Unraid**: `.../appdata/emby/plugins/` (inside the container: `/config/plugins/`)
3. Restart the Emby server.
4. Dashboard → **Plugins** → **Simkl TV Tracker** → **Settings**.

> 💡 **After updating the plugin**, do a **hard refresh** of the browser
> (Ctrl + Shift + R) on the settings page: Emby caches the configuration page
> aggressively.

---

## Configuration

In **Plugins → Simkl TV Tracker → Settings**:

1. Select the Emby user to configure.
2. Click **Log In**, open the link (the PIN is pre-filled), approve the access on
   Simkl, then come back to the page. Your Simkl profile name appears once logged in.
3. Adjust the options and **Save**.

### Options (per user)
| Option | Purpose |
|--------|---------|
| **Scrobble Movies / TV Shows** | Real-time playback reporting. |
| **Watched threshold (%)** | Percentage above which an item counts as watched. |
| **Export watched status** | Pushes watched state (scheduled task + manual toggle). |
| **Import resume points** | Recreates resume positions from Simkl. |
| **Sync ratings** | Movie & show ratings (Simkl has no season/episode ratings). |
| **Plan to watch** | Adds present-but-unwatched titles to the “to watch” list. |
| **Don't mark unwatched…** | *(Recommended, checked)* prevents Emby from clearing local watched state when a title is absent from Simkl. |
| **Extra logging** | Verbose logging for troubleshooting. |
| **Excluded folders** | Library folders ignored for this user. |

---

## Scheduled tasks

Dashboard → **Scheduled Tasks**, **Simkl** category:

- **Sync library to Simkl** — sends watched state, ratings and (optionally) the
  watchlist to Simkl.
- **Import playstates from Simkl** — brings back into Emby the watched state, play
  count, dates and, above all, the **resume points**.

Set their frequency (e.g. daily). Real-time scrobbling works automatically as soon
as playback starts or stops.

---

## How it works

- **Scrobbling**: on playback start → `/scrobble/start` (“watching”); on stop →
  `/scrobble/stop` if finished (≥ 80 % = marked watched by Simkl), otherwise
  `/scrobble/pause`, which **saves a resume point**.
- **Manual toggle**: checking “played” / “unplayed” in Emby pushes to
  `/sync/history` or `/sync/history/remove`.
- **Export** (task): movies/episodes watched locally but missing on Simkl are added
  to history; ratings and watchlist follow the chosen options.
- **Import** (task): reads `/sync/all-items` and `/sync/playback` to report watched
  state, ratings and resume points back into Emby.

> If an episode is marked “watched” in Emby **without a play date**, Simkl stamps it
> at send time, so it shows up in today’s history. This is expected (Emby did not
> keep the original date).

---

## Design decisions

- **Trakt “collection” is ignored**: Simkl has no equivalent to the *owned* concept
  (distinct from *watched*), so it is deliberately left out.
- **No deletion on Simkl**: unlike the Trakt plugin, the export task **never
  removes** history from Simkl, to avoid any accidental data loss. To unmark a
  title, use the Simkl website.

---

## Building from source

Requirements: **.NET SDK** (8.x works). Target: `netstandard2.0`.
```bash
cd Simkl-Emby
dotnet build -c Release
```
The plugin is produced at `Simkl-Emby/bin/Release/netstandard2.0/Simkl.dll`.

---

## Architecture

```
Simkl-Emby/
  Enums.cs                     MediaStatus (start / pause / stop)
  Plugin.cs                    plugin declaration + config pages
  Configuration/               UserConfig, PluginConfiguration, configPage.html + configPage.js
  API/
    SimklApi.cs                scrobble, history(+remove), ratings, add-to-list, all-items, playback
    Objects/                   write payloads (SyncItems, ScrobblePayloads) + read models (AllItems, PlaybackSession)
    Responses/                 OAuth / history responses
    ServerEndpoint.cs          /Simkl/oauth/... routes used by the config page
  Helpers/                     UserHelper, Match (Emby ↔ Simkl), SyncHelper (ids, CanSync, chunking)
  Services/Scrobbler.cs        ServerMediator: playback events + watched/unwatched toggle
  ScheduledTasks/              SyncToSimklTask (export) + SyncFromSimklTask (import)
```

Simkl endpoints used: `/oauth/pin`, `/scrobble/{start,pause,stop}`,
`/sync/history` (+`/remove`), `/sync/ratings`, `/sync/add-to-list`,
`/sync/all-items`, `/sync/playback`, `/users/settings`.

---

## Credits
- Original plugin: [SIMKL/Emby](https://github.com/SIMKL/Emby) (David Davó).
- Feature blueprint: [Trakt for Emby](https://github.com/MediaBrowser/trakt).

## Links
- Bugs & requests: https://github.com/Guiro28/Simkl-Emby/issues
- Simkl: https://simkl.com/ · Simkl Discord: https://discord.gg/JRtwsfG

[license]: https://github.com/Guiro28/Simkl-Emby/blob/master/LICENSE
