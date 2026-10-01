# Bing Smith

Replaces the sound played when Smith upgrades a card at a rest site in Slay the Spire 2. The replacement audio is embedded in the DLL from `bing-bing-bing.mp3`.

## Build

Requires .NET SDK 10, .NET 9 reference packs, and a local Slay the Spire 2 installation. Update the three `HintPath` values in `BingSmith/BingSmith.csproj` if the game is installed elsewhere.

```powershell
dotnet build BingSmith/BingSmith.csproj -c Release
```

## Install

Install RitsuLib 0.6.3 or newer. Place `BingSmith.dll` and `BingSmith.json` together in a `BingSmith` folder under the game's `mods` directory, next to `SlayTheSpire2.exe`. Enable the mod from the game's mod menu. In Mod Settings, open Bing Smith to toggle the replacement, set volume (0-200%), or play a preview.

This mod targets game version 0.107.1. After a game update, rebuild and verify that the Smith sound still uses `card_smith.mp3`.
