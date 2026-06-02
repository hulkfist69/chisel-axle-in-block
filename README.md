# Axle Chisel

Vintage Story 1.20+ mod. Goal: make the "axle in block" feature compatible with chiseled (microblock) blocks.

## What this should do

- **Chisel around an axle** — take a block that has an axle running through it and chisel its geometry. The axle keeps spinning and stays networked.
- **Place a pre-chiseled block on an axle** — drop a microblock onto a position that has an axle, the microblock takes over visually while the axle BE persists underneath.

## Status

v0.1.0 — scaffolding only. The first build dumps the structure of the relevant VS internal classes (`BlockAxle`, `BlockEntityMicroBlock`, etc.) to the log so we can plan the actual integration around what 1.22 actually exposes.

## Build

```bash
# Windows
.\build.ps1

# macOS / Linux
./build.sh
```

Output lands in `dist/AxleChisel-<version>.zip`. Drop into `%APPDATA%\VintagestoryData\Mods\` (Windows) / `~/Library/Application Support/VintagestoryData/Mods/` (macOS).

## Next steps

1. Run once and paste the `[axlechisel] type found / NOT FOUND` lines from `client-main.log`.
2. Based on which class names actually exist in 1.22, plan the Harmony hooks: probably postfix on the chiseling operation (preserve axle BE) and on the microblock place handler (allow placement on an axle position).
