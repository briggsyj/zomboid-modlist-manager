# AGENTS.md

Guidance for AI coding agents (and humans) working in this repo.

## What this is

A .NET 10 Blazor Web App (Interactive Server) for managing Steam Workshop mod requests for a
Project Zomboid server. SQLite for storage, MudBlazor for UI, cookie auth for a single admin.
See [README.md](README.md) for features and how to run it.

```
src/ModlistManager/
  Components/Pages/   Home (Requests), Backlog, Modlist (Active Mods), Login, Admin/Dashboard (Manage)
                      Routes: / , /backlog, /active-mods, /manage - the old /modlist and /admin still resolve
  Data/               EF Core entities, DbContext, migrations
  Services/           Request logic, Mod ID lookup, parsers
tests/ModlistManager.Tests/
```

## Commands

```bash
dotnet build
dotnet test
docker compose up -d --build          # run it (http://localhost:8066)
cd src/ModlistManager && dotnet ef migrations add <Name> -o Data/Migrations
```

## Commit conventions

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<optional scope>): <description>

<optional body explaining why, not what>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `build`, `ci`, `perf`.

- Subject in imperative mood, lowercase, no trailing full stop, ideally under ~72 chars.
- Use the body for *why* a change was made and anything non-obvious a reviewer would otherwise
  have to reverse-engineer. Don't restate the diff.
- A breaking change gets a `!` (`feat(api)!: ...`) and a `BREAKING CHANGE:` footer.
- Scopes in use: `requests`, `modlist`, `admin`, `auth`, `docker`, `ci`, `data`.

Release tags are semver, unprefixed (`0.3.0`), and publishing an image is triggered by pushing one.

## Things that will bite you

These were all found the hard way. Please don't undo them.

**`dotnet run` serves empty static assets.** On .NET runtime 10.0.3, every static asset comes back
as an empty `200`, so `blazor.web.js` never loads, no interactivity works, and there is no error
anywhere. Docker is unaffected (runtime 10.0.11). Test UI behaviour against the published output or
Docker, not `dotnet run`.

**Never add a csproj-only restore step to the Dockerfile.** The usual layer-caching trick
(`COPY *.csproj` → `dotnet restore` → copy the rest → `--no-restore`) silently breaks Static Web
Assets discovery: `blazor.web.js` and every other asset vanish from the publish output, the app
serves empty files, and nothing in the build logs complains.

**Forms are plain `<form method="post">` posting to minimal API endpoints, not `EditForm`.** This is
deliberate: submitting a request and logging in must work even when the SignalR circuit hasn't
connected. `EditForm` renders an antiforgery-token fallback form that nothing handles server-side,
which returns a bare 400. Inputs still need `@bind` so a re-render (e.g. typing in the autocomplete)
doesn't wipe the other fields, and MudBlazor inputs need an explicit `name=` to take part in the POST.

**SteamCMD cannot run on arm64 hosts.** It only ships a 32-bit x86 binary, which neither Rosetta nor
QEMU can execute, so it exits 1 with no output at all. It's off by default; Mod IDs come from the
Steam Workshop API instead. Don't reintroduce it as the primary path.

**Give new non-nullable columns a sensible default in the migration.** `Mod.IsActive` is mapped with
`HasDefaultValue(true)` so existing rows stay active on upgrade - without it a bool column defaults
to `false` and would have silently emptied every server's `Mods=` export.

**MudTable `Breakpoint`** collapses to a stacked mobile layout at and below the given size. Use
`Breakpoint.Sm`; `Md` triggers on ordinary ~1280px laptop screens.

**`MudButton Href=""` renders a `<button>`, not a link,** so it silently does nothing. Use `Href="/"`.

## Working style

- Verify rather than assume: this app's failures have mostly been invisible to `dotnet build`.
  Browser-level checks (Playwright) caught bugs that curl could not, because curl doesn't run JS.
- Prefer adding a test over a manual check when the logic is in `Services/`.
- Don't commit `.env` (gitignored - it holds `ADMIN_PASSWORD`) or `*.db` files.
- The exports are the product: `WorkshopItems=` includes every approved mod, `Mods=` includes only
  *active* ones. Changing either asymmetry breaks servers, so cover it with tests.
