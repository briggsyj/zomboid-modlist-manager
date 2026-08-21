# Modlist Manager

A small .NET 10 Blazor Web App for managing Steam Workshop mod requests for a Project Zomboid server.

## Features

- Anyone can submit a mod request (title, Steam Workshop link or item ID, requester name with
  autocomplete over previously-used names).
- Each request is attached to a `Mod` record (Steam Workshop item + game), deduplicated by
  (game, workshop ID) - if two people request the same workshop item, both requests point at the
  same Mod rather than tracking separate copies of its fetch status/Mod IDs.
- A single admin account (password-protected) can approve, backlog, or decline requests.
- When a request creates a new Mod, the app shells out to `steamcmd` in the background to
  anonymously download the workshop item and read its `mod.info` file(s), discovering the real
  Project Zomboid Mod ID(s) (which differ from the Steam Workshop item ID, and a single workshop
  item can bundle several mods). This can fail (SteamCMD missing, item isn't a valid PZ mod,
  non-standard layout, etc) - in which case an admin can add/edit Mod IDs manually and retry the fetch.
- Approving a request adds its Mod to the modlist for that game; if another request for the same
  Mod is later un-approved, the Mod only leaves the modlist once no approved request references it
  anymore.
- Admin pages:
  - `/admin` - review queue with approve/backlog/decline actions, per-mod Mod ID list/editor, and
    a retry button when the automatic fetch fails.
  - `/admin/approved` - the modlist for a selected game, plus two clipboard-copy buttons:
    - all Steam Workshop item IDs currently on a modlist (any game), semicolon-delimited
    - Project Zomboid Mod IDs currently on the Project Zomboid modlist, semicolon-delimited and
      each prefixed with `\` (the format PZ server configs expect for `Mods=`)
- Data is stored in SQLite - no external database required.

## Tech stack

- .NET 10, ASP.NET Core Blazor Web App (Interactive Server render mode)
- EF Core + SQLite
- Cookie authentication for the single admin account (no self-registration)
- SteamCMD (anonymous login) for Mod ID discovery

## Running locally

Prerequisites: .NET 10 SDK, and (optionally, for Mod ID auto-discovery) [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD).

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

### SteamCMD (Mod ID auto-discovery)

Configure these under the `SteamCmd` section in `appsettings.json` (or matching
`SteamCmd__ExecutablePath` / `SteamCmd__WorkshopContentRoot` environment variables):

- `ExecutablePath` - path to the `steamcmd` executable (defaults to `steamcmd` on PATH).
- `WorkshopContentRoot` - the directory SteamCMD treats as its own home/install directory (the one
  under which it creates `steamapps/workshop/content/108600/<item id>` after a download). This
  varies by how SteamCMD was installed, so it's not defaulted - the app fails each fetch with a
  clear message until it's set.

If SteamCMD isn't installed or configured, mod requests still work fine - the fetch just fails
gracefully and an admin can add the Mod ID(s) manually from `/admin`.

### Tests

```bash
dotnet test
```

## Running with Docker

`Dockerfile` builds the app and installs SteamCMD into the runtime image, so Mod ID auto-discovery
works out of the box. SteamCMD only ships an x86/i386 Linux build, so the image is pinned to
`linux/amd64` in `docker-compose.yml` (this also makes it work under emulation on Apple Silicon).

```bash
cp .env.example .env   # then edit .env and set a real ADMIN_PASSWORD
docker compose up -d --build
```

The SQLite database lives at `/data/modlist.db` inside the container, backed by the named
`modlist-data` Docker volume - so it survives container rebuilds/recreation (`docker compose down`
followed by `docker compose up` keeps your data; only `docker volume rm` deletes it).

## Project structure

```
src/ModlistManager/        The Blazor Web App
  Components/Pages/        Home (public), Login, Admin/Dashboard, Admin/Approved
  Data/                    EF Core entities, DbContext, migrations
  Services/                Business logic, SteamCMD background fetch, parsers
tests/ModlistManager.Tests/  Unit/integration tests (xUnit)
```
