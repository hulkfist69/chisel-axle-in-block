# Axle Chisel

[![Release](https://img.shields.io/github/v/release/hulkfist69/chisel-axle-in-block?sort=semver)](https://github.com/hulkfist69/chisel-axle-in-block/releases)
[![License](https://img.shields.io/github/license/hulkfist69/chisel-axle-in-block)](LICENSE)
[![Issues](https://img.shields.io/github/issues-raw/hulkfist69/chisel-axle-in-block)](https://github.com/hulkfist69/chisel-axle-in-block/issues)

Vintage Story 1.20+ mod. Goal: make the "axle in block" feature compatible with chiseled (microblock) blocks.

## What this should do

- **Chisel around an axle** — take a block that has an axle running through it and chisel its geometry. The axle keeps spinning and stays networked.
- **Place a pre-chiseled block on an axle** — drop a microblock onto a position that has an axle, the microblock takes over visually while the axle BE persists underneath.

## Status

v0.1.0 — scaffolding only. The first build dumps the structure of the relevant Vintage Story internal classes (`BlockAxle`, `BlockEntityMicroBlock`, etc.) to the log so we can plan the actual integration around what 1.22 actually exposes.

## Quick start

```bash
git clone https://github.com/hulkfist69/chisel-axle-in-block.git
cd chisel-axle-in-block
export VINTAGE_STORY="/path/to/Vintagestory"
./build.sh
```

For Windows:

```powershell
$env:VINTAGE_STORY = "C:\path\to\Vintagestory"
.\build.ps1
```

The build output is saved to `dist/AxleChisel-<version>.zip`. Install it by copying the zip into the Vintage Story mods folder:

- Windows: `%APPDATA%\VintagestoryData\Mods\`
- macOS: `~/Library/Application Support/VintagestoryData/Mods/`

## Build

```bash
# Windows
.\build.ps1

# macOS / Linux
./build.sh
```

### Short commands

`.cmd` shims let you skip the `.ps1`, so from the repo root you can run:

```powershell
.\dev            # build + redeploy + launch (alias for dev.ps1)
.\build          # build the zip only      (alias for build.ps1)
.\log            # push VS logs to the repo (alias for pushlogs.ps1)
```

Args still pass through, e.g. `.\dev -PushLogs` or `.\log -Branch test-logs`.

Want to drop the `.\` and just type `dev` / `build` / `log`? Run `notepad $PROFILE`
once and paste this (works whenever your current dir is a mod project root):

```powershell
function dev   { & "$PWD\dev.ps1"      @args }
function build { & "$PWD\build.ps1"    @args }
function log   { & "$PWD\pushlogs.ps1" @args }
```

### Dev hot-loop (Windows)

`dev.ps1` runs the full iterate-and-test cycle in one command: it closes any
running Vintage Story, builds the mod, removes the old install from your Mods
folder, copies the fresh zip in, and relaunches the game.

```powershell
.\dev            # build + redeploy + launch
.\dev -NoLaunch  # build + redeploy, but don't start the game
.\dev -PushLogs  # launch, then auto-push VS logs when you close the game
```

It uses `$env:VINTAGE_STORY` (your VS install dir) and defaults the Mods folder
to `%APPDATA%\VintagestoryData\Mods` — override with `$env:VINTAGE_STORY_DATA`
if your data path is custom.

### Sending logs back for review

`pushlogs.ps1` copies the Vintage Story logs into `logs/` in this repo, writes a
filtered `logs/axlechisel-filtered.log` containing only `[axlechisel]` lines,
then commits and pushes them — so they can be pulled and read remotely instead
of copy-pasting by hand.

```powershell
.\log                    # copy logs, commit, push to main
.\log -Branch test-logs  # push to a separate branch instead
```

The easiest workflow is `.\dev -PushLogs`: it builds, deploys, launches, and
then waits — when you quit the game it pushes the logs automatically.

## Development

This repository includes:

- `AxleChisel.csproj` — .NET 10 mod project
- `src/AxleChiselModSystem.cs` — mod lifecycle and Harmony bootstrap
- `src/RuntimePatches.cs` — runtime discovery and future Harmony integration
- `modinfo.json` — mod metadata and required game version
- `assets/axlechisel/lang/en.json` — localization strings

### Recommended workflow

1. Create a branch from `main` for each change.
2. Build with `./build.sh` or `.uild.ps1`.
3. Install the resulting ZIP and launch Vintage Story.
4. Copy any `[axlechisel]` log output into the issue or PR.

## Contributing

Contributions are welcome. Use the issue templates and PR checklist to keep work organized.

- Bug reports: submit a bug report with reproduction steps, Vintage Story version, and any `[axlechisel]` log lines.
- Feature requests: explain the desired outcome and why it improves the mod.
- Pull requests: one logical change per PR, include build/test notes, and keep the branch scoped.

## License

This project is licensed under the MIT License. See `LICENSE` for details.
