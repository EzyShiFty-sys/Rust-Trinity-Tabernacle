# Trinity Tabernacle

An Oxide/uMod (and Carbon-compatible) Rust server plugin that automatically spawns an indestructible, no-decay church (**Trinity Tabernacle**) on flat terrain every wipe.

## Features

- **Auto-Spawn on Wipe**: Hooks `OnNewSave` and `OnServerInitialized` to automatically spawn once per wipe and persist entity IDs in `oxide/data/TrinityTabernacle.json` across server restarts.
- **Flat-Terrain Finder**: Searches outward from the map center for flat, dry ground away from monuments and cliffs, and clears overlapping trees/nodes in the building footprint.
- **Sanctuary Layout**: Builds a 3×5 Stone-grade sanctuary with front steps and porch, pitched roof with vaulted ceiling, front steeple/belfry, center aisle rugs, sittable pews, altar table, hearth fireplace, and perpetually lit lanterns.
- **Indestructible & No Decay**: Blocks damage, decay, upgrading, demolishing, pickup, fuel consumption, and nearby player building (configurable radius).

## Installation

1. Download `TrinityTabernacle.cs`.
2. Place it in your server's `oxide/plugins/` directory (or `carbon/plugins/` for Carbon).
3. Oxide/Carbon will compile and load the plugin automatically, generating `oxide/config/TrinityTabernacle.json` on first load.

## Permissions

- `trinitytabernacle.admin` — Required for admin chat commands (`/spawntabernacle`, `/removetabernacle`) and bypassing the nearby building restriction. Server admins (`IsAdmin`) also have access by default.

Example:
```text
oxide.grant user <steamid> trinitytabernacle.admin
```

## Commands

### Chat Commands
- `/church` — Displays the map grid and coordinates of the Trinity Tabernacle to players (if enabled in config).
- `/spawntabernacle` — Spawns the church at your current position and facing direction (admin).
- `/spawntabernacle auto` — Automatically finds the best flat terrain on the map and spawns the church there (admin).
- `/removetabernacle` — Removes the currently spawned church (admin).

### Console Commands
- `tabernacle.spawn` — Automatically finds flat terrain and spawns the church.
- `tabernacle.remove` — Removes the currently spawned church.
- `tabernacle.info` — Prints the current spawn status, grid location, coordinates, and entity count.

## Configuration (`oxide/config/TrinityTabernacle.json`)

```json
{
  "Church Name": "Trinity Tabernacle",
  "Auto Spawn On Wipe": true,
  "Building Grade (Twigs, Wood, Stone, Metal, TopTier)": "Stone",
  "Indestructible & No Decay": true,
  "Prevent Player Building Radius (meters, 0 to disable)": 25.0,
  "Keep Altar Lights & Hearth Lit": true,
  "Max Terrain Slope (meters)": 1.4,
  "Min Distance From Monuments (meters)": 130.0,
  "Map Search Radius Percent (0.1 - 0.85)": 0.65,
  "Allow /church Command For Players": true
}
```
