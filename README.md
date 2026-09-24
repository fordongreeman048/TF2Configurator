# TF2 Configurator

A Windows app for setting up [mastercomfig](https://mastercomfig.com/) in Team Fortress 2.
Instead of hand-writing cvars, you pick options in a UI. It edits the files mastercomfig
actually reads — `tf\cfg\overrides\modules.cfg`, `autoexec.cfg` and the per-class configs —
and has a launch-options builder that leaves Steam's own files alone.

Read this in: [English](README.md) · [Русский](README.ru.md)

## Screenshots

Modules — every mastercomfig module with its levels, costs and descriptions:

![Modules page](docs/screenshots/modules.png)

autoexec.cfg and the per-class config editors:

![autoexec.cfg page](docs/screenshots/autoexec.png)
![Class configs page](docs/screenshots/class.png)

Launch options builder:

![Launch options page](docs/screenshots/launch.png)

## What it does

- **Modules** — every mastercomfig module in the bundled catalog (56 at the moment), with
  its levels, CPU/GPU cost, notes and tooltips. Category sidebar, search, and an issues panel
  that checks an existing `modules.cfg` against the current catalog and *suggests* fixes
  (unknown modules, renamed levels, duplicate keys). It never changes anything by itself.
- **Presets** — apply the mastercomfig presets (destitute → ultra, plus custom) in one click,
  the same way comfig.app does. It only changes the pickers; saving is still a separate step
  you can review.
- **Module details** — the "Details…" button on a module shows the console variables each
  level sets, straight from mastercomfig's own data.
- **autoexec.cfg** — a curated list of the cvars people actually tune (networking, frame
  rate, viewmodels, mouse, crosshair, HUD, gameplay), with a warning when a cvar overrides a
  mastercomfig module. You can also edit the raw text for binds, aliases and anything else.
- **Class configs** — the same editor for all nine class files, with per-class switching
  that never silently throws away unsaved work.
- **Launch options** — builds a launch string with two-way checkbox/text sync, a safe
  recommended set, and cautions on the risky flags. It never writes Steam's
  `localconfig.vdf`: you copy the line into Steam, or use **Launch TF2 once** to apply it to
  a single launch via `steam://run/440`.

### Safety

- **Round-trip fidelity** — files are never regenerated. Line endings, the final newline,
  comments, blank lines and unknown keys survive untouched unless you actually change them.
  Unknown levels in a file are kept and flagged, not silently dropped.
- **Backups** — every save first copies the previous file into
  `%AppData%\TF2Configurator\backups` (kept for 30 days, max 60 folders). The **Backups…**
  button browses, previews and restores any of them; restoring backs up the current file
  first, so it's reversible.
- **Offline-first** — the catalog and preset data are embedded, and can be refreshed from
  mastercomfig's repository. Downloads are parsed and validated before they're cached, so a
  bad download can't break startup; a quiet check looks for updates a few days after the
  last one.
- **Crash surface** — unhandled exceptions show a dialog and get written to
  `%AppData%\TF2Configurator\crash.log`; an error never touches your configs.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build
- Team Fortress 2 with [mastercomfig](https://mastercomfig.com/download) installed
  (the app finds it via the Steam registry, library folders, or you point it at the folder)

## Build and run

```powershell
dotnet build TF2Configurator.slnx
dotnet run --project src\TF2Configurator
```

Tests and the on-machine check:

```powershell
dotnet test tests\TF2Configurator.Tests
dotnet run --project tools\SmokeTest          # optional: pass a TF2 root as an argument
```

SmokeTest runs the real parsers and validator against an actual install and verifies the
round-trip guarantees. It never writes to the game folder.

## Refreshing the bundled data

The app refreshes its per-user catalog at runtime, but the *embedded* snapshots are what
ship. When mastercomfig changes its data, refresh them with:

```powershell
powershell -ExecutionPolicy Bypass -File tools\update-catalog.ps1
```

This downloads `modules.json`, `preset_modules.json` and `module_values.json` from
mastercomfig's develop branch, validates each as JSON, and writes them to `data\` and
`src\TF2Configurator\Assets\`. Rebuild afterwards.

## Layout

| Path | Purpose |
|---|---|
| `src/TF2Configurator/` | The app (WinForms, net10.0-windows) |
| — `Models/` | Catalog/preset parsers, line-preserving cfg document editors |
| — `Services/` | TF2 detection, catalog service, safe writer + validator, cvar/launch-option catalogs |
| — `Forms/` | Main window, the four pages, backup browser, text/diff dialogs |
| — `Assets/` | Embedded snapshots of mastercomfig's data + app icon |
| `tools/SmokeTest/` | Read-only integration harness against a real TF2 install |
| `tools/update-catalog.ps1` | Refreshes the bundled data from upstream |
| `tests/TF2Configurator.Tests/` | xunit tests for parsing, editing and validation |
| `data/` | Working copies of the mastercomfig JSON files |

## Design notes

- A config file's own conventions belong to the file: LF files stay LF, a missing final
  newline isn't invented, and duplicate keys read and edit the *last* assignment (the one
  the engine actually uses).
- The issue panel and the suggestion buttons never modify a file; every suggestion still
  goes through the normal save (with backup and diff preview).
- Launch options aren't written to Steam because Steam rewrites `localconfig.vdf` on exit
  and would usually throw the edit away — so the app offers copy-paste and a one-shot
  `steam://run/440//` launch instead.

## AI assistance

This project was developed with help from an AI coding assistant. It helped generate and
review code, write and port tests, and draft parts of this documentation. Everything was
reviewed and adjusted by a human before being committed:

- every change was reviewed manually before commit
- the round-trip and parsing guarantees are locked in by the test suite under
  `tests/TF2Configurator.Tests`
- nothing here ships as unexamined AI output — the maintainer is responsible for the final
  state of the code either way