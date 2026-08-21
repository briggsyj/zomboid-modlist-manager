# Modlist Manager

[![CI](https://github.com/briggsyj/zomboid-modlist-manager/actions/workflows/ci.yml/badge.svg)](https://github.com/briggsyj/zomboid-modlist-manager/actions/workflows/ci.yml)
[![License: GPL v2](https://img.shields.io/badge/License-GPL%20v2-blue.svg)](LICENSE)

A small .NET 10 Blazor Web App for managing Steam Workshop mod requests for a Project Zomboid server.

Players ask for mods through a simple form; an admin approves, backlogs or declines them; the
approved set is exported as the two semicolon-delimited lists a PZ server config needs.

## Features

- **Anyone can submit a mod request** - a Steam Workshop link or item ID, their name (autocompleted
  from previously-used names), and an optional reason explaining why the server needs it.
- **Mod names come from the workshop, not the requester.** There's no title field to mistype or
  disagree on: the name is read from the workshop item and previewed on the form as soon as a link
  is entered, so you can confirm you pasted the right thing before submitting.
- **Duplicate detection while you type.** Paste a workshop link or ID and the form immediately tells
  you if that item has been requested before - matching on the parsed workshop ID, so a full URL and
  a bare ID are recognised as the same item. Submitting anyway is allowed; it counts as another vote.
- **Mod IDs are resolved automatically.** Project Zomboid's `Mods=` server setting needs the mod's
  internal Mod ID, which is *not* the workshop item ID. The app reads it from the workshop item's
  description, which by PZ convention ends with `Workshop ID: ...` / `Mod ID: ...` lines (packs that
  bundle several mods list one line each). The item's real title is picked up at the same time.
- **Requests are attached to a `Mod` record** (workshop item + game), deduplicated by
  (game, workshop ID) - two people requesting the same item share one Mod rather than each tracking
  their own copy of its fetch status and Mod IDs.
- **A single password-protected admin** can approve, backlog or decline requests, edit Mod IDs by
  hand, and retry a failed lookup.
- **Approving adds the Mod to that game's modlist.** If another request for the same Mod is later
  un-approved, the Mod only leaves the modlist once no approved request references it.
- **Mods can be parked without being removed.** An admin-only *Active* checkbox on the modlist
  disables a mod: it stays listed and still downloads, but stops being loaded. See below.
- Data is stored in SQLite - no external database required.

### Pages

| Page | Who can see it |
| --- | --- |
| `/` - request form + pending requests | everyone |
| `/modlist` - approved mods for a game | everyone; the export buttons and *Active* toggle are admin-only |
| `/login` - admin sign-in | everyone |
| `/admin` - review queue, Mod ID editing, retry fetch | admin only |

### Exports

The two buttons on `/modlist` copy a semicolon-delimited list to the clipboard, ready to paste into
your server config:

| Button | Contents | Server setting |
| --- | --- | --- |
| Copy Workshop IDs | every approved Steam Workshop item ID, **active or not** | `WorkshopItems=` |
| Copy Zomboid Mod IDs | every **active** approved PZ Mod ID, each prefixed with `\` | `Mods=` |

### Active vs inactive

Unticking *Active* on a mod drops its Mod ID from `Mods=` while leaving its workshop ID in
`WorkshopItems=`. That's how a Project Zomboid server disables a mod: the item is still downloaded,
it just isn't loaded - so switching it back on needs no re-download and no player-side re-sync.

Mods are active when first approved, and inactive mods stay visible on the modlist.

## Tech stack

- .NET 10, ASP.NET Core Blazor Web App (Interactive Server render mode)
- [MudBlazor](https://mudblazor.com/) for the UI
- EF Core + SQLite
- Cookie authentication for the single admin account (no self-registration)
- Steam Workshop web API for Mod ID discovery; SteamCMD optionally, see below

## Running locally

Prerequisites: .NET 10 SDK.

```bash
export ADMIN_PASSWORD=your-password-here   # required; hashed and (re)seeded into the DB on every startup
cd src/ModlistManager
dotnet run
```

The app listens on the URL(s) in `Properties/launchSettings.json` (or pass `--urls`). SQLite data is
written to `modlist.db` in the app's working directory by default - override with the
`ConnectionStrings__Default` environment variable or `ConnectionStrings:Default` in
`appsettings.json`, e.g. `Data Source=/path/to/modlist.db`.

Migrations are applied automatically on startup. To add a new migration after changing an entity:

```bash
cd src/ModlistManager
dotnet ef migrations add <Name> -o Data/Migrations
```

> **Note:** `dotnet run` on .NET 10.0.3 has a known problem serving static assets (every file comes
> back as an empty `200`, which stops Blazor from booting). If the UI renders unstyled and buttons do
> nothing locally, run the published output instead - `dotnet publish -c Release -o out && dotnet
> out/ModlistManager.dll` - or just use Docker. Docker is unaffected; it ships runtime 10.0.11.

### How Mod ID lookup works

When a request introduces a new workshop item, a background service resolves its Mod ID(s):

1. It calls Steam's public `GetPublishedFileDetails` API (no API key needed) for the item's title
   and description.
2. It extracts every `Mod ID: ...` line from the description.

If the description doesn't state a Mod ID, the fetch is marked failed with an explanation and an
admin can type the Mod ID in by hand from `/admin` (or hit **Retry fetch**). Requests themselves are
never blocked by a failed lookup.

### Optional: SteamCMD

SteamCMD reads the authoritative Mod ID out of each mod's `mod.info` rather than trusting the
description. It's **off by default** for two reasons: it downloads the entire mod just to read one
small file, and it only ships a 32-bit x86 binary, so it cannot execute on an arm64 host emulating
amd64 (i.e. Docker on Apple Silicon) - it exits immediately with no output.

To use it on a real amd64 host:

```bash
docker compose build --build-arg INSTALL_STEAMCMD=true
# then add to the service's environment in docker-compose.yml:
#   SteamCmd__Enabled: "true"
```

Or locally, install [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD) and set under the
`SteamCmd` section (or as `SteamCmd__*` environment variables):

- `Enabled` - `true` to try SteamCMD first.
- `ExecutablePath` - path to the `steamcmd` executable (defaults to `steamcmd` on PATH).
- `WorkshopContentRoot` - the directory SteamCMD treats as its install root, under which it creates
  `steamapps/workshop/content/108600/<item id>`. This varies by install method, so it has no default.

When SteamCMD is enabled but fails, the app falls back to the API automatically and records why in
the fetch log shown on `/admin`.

### Tests

```bash
dotnet test
```

## Running with Docker

```bash
cp .env.example .env   # then edit .env and set a real ADMIN_PASSWORD
docker compose up -d --build
```

The SQLite database lives at `/data/modlist.db` inside the container, backed by the named
`modlist-data` Docker volume, alongside the Data Protection keys at `/data/keys` (so admin sessions
survive a restart). Data persists across `docker compose down` / `up`; only `docker volume rm`
deletes it.

The image is pinned to `linux/amd64` in `docker-compose.yml`, which is only actually required for
the optional SteamCMD path.

## CI / releases

- **`ci.yml`** runs on every pull request and push to `main`: restores, builds, runs the test suite,
  and separately builds the Docker image (without publishing) - the Dockerfile has broken before in
  ways `dotnet build` can't catch.
- **`release.yml`** runs on `v*` tags: runs the tests, then builds and pushes a multi-arch
  (`linux/amd64` + `linux/arm64`) image to the GitHub Container Registry.

To cut a release:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

That publishes `ghcr.io/briggsyj/zomboid-modlist-manager` tagged `1.0.0`, `1.0`, `1` and `latest`.
No secrets to configure - it authenticates with the built-in `GITHUB_TOKEN`. Note that GHCR packages
default to private; make the package public from its GitHub page if you want unauthenticated pulls.

The arm64 image is only possible while SteamCMD stays out of the image (it's 32-bit x86 and its
i386 dependencies have no arm64 build), so an `INSTALL_STEAMCMD=true` image must be amd64-only.

## Project structure

```
src/ModlistManager/          The Blazor Web App
  Components/Pages/          Home, Modlist, Login, Admin/Dashboard
  Data/                      EF Core entities, DbContext, migrations
  Services/                  Request logic, Mod ID fetching, parsers
tests/ModlistManager.Tests/  Unit/integration tests (xUnit)
```

## License

Copyright (C) 2026 John Briggs.

This program is free software; you can redistribute it and/or modify it under the terms of the
**GNU General Public License, version 2** as published by the Free Software Foundation. The full
text is in [LICENSE](LICENSE).

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
General Public License for more details.

Third-party dependencies (MudBlazor, EF Core and the rest of the .NET stack) are MIT-licensed and
GPL-compatible. Project Zomboid and Steam are trademarks of their respective owners; this project is
not affiliated with either.
