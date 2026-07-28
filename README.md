# Tower Stats Mod

A BepInEx mod for **Sineus Arena** that displays real-time Kills and current DPS overlays directly above offensive towers.

## Features

- **Overhead Badge**: Displays a 2-line floating UI badge directly above towers:
  - Top line: `⚔ Kills: X`
  - Bottom line: `⚡ DPS: Y` (calculated over a rolling 5-second window)
- **Always On Top (Z-Test Bypass)**: UI badges render on top of 3D geometry so they are never occluded by tower models.
- **Towers Only**: Only displays stats on offensive towers (Archer Towers, Mage Towers, Catapults, etc.). Non-attacking structures like Barracks, Abodes, Houses, and Storage are filtered out.
- **Dynamic Box Width**: Background badge automatically expands and contracts based on text width.
- **Distance Culling**: Automatically hides UI badges for towers further than 32 units away from the player character.
- **Object Pool & Respawn Safe**: Properly handles recycled enemy unit pools and hero respawns after player death.
- **Multi-Tower Precision**: Resolves standalone projectiles directly to their parent tower for accurate kill and damage attribution across multiple active towers.
- **Toggle Display**: Press **F6** in-game to toggle the overlay on or off anytime.

## Requirements

- [BepInEx 5.x](https://github.com/BepInEx/BepInEx)

## Installation

1. Download `TowerStatsMod.dll` from the [Releases](../../releases/latest).
2. Move `TowerStatsMod.dll` into your BepInEx plugins folder:
   ```
   Sineus Arena/BepInEx/plugins/TowerStatsMod/TowerStatsMod.dll
   ```
   *(or your r2modman profile plugins folder)*

## Configuration

After running the game once with the mod installed, a config file is generated at `BepInEx/config/com.antigravity.towerstatsmod.cfg`:

| Setting | Default | Description |
|---|---|---|
| `ToggleKey` | `F6` | Key to toggle stats overlay on/off |
| `ShowRadius` | `32.0` | Max distance from player character to render badges |
| `HeightOffset` | `4.0` | Vertical offset above tower base |
| `UiScale` | `0.019` | Scale multiplier for the UI badge |
| `FontSize` | `16` | Font size of text elements |
| `DpsWindowSeconds` | `5.0` | Time window in seconds for rolling DPS calculation |
| `RenderThrough` | `true` | Render badge through 3D geometry |

## Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/Snack-tacular/TowerStatsMod.git
   ```
2. Build with .NET SDK:
   ```bash
   dotnet build -c Release
   ```
