# Slay the Spire 2 Modding Notes

## Local Game Facts

- The installed game is Slay the Spire 2, app id `2868840`.
- The verified local build is `v0.107.1`.
- The game is a Godot/.NET application. Its managed game assembly is `data_sts2_windows_x86_64/sts2.dll`.
- The game currently uses .NET 9 assemblies. Build mods with a .NET 9 target framework and an SDK that provides the .NET 9 reference pack. This machine uses SDK `10.0.303` with `net9.0`.
- Game installation paths are machine-specific. The current installation is `E:\SteamLibrary\steamapps\common\Slay the Spire 2`.

## Mod Layout

- A DLL mod consists of a folder under the game's `mods` directory containing `<id>.dll` and `<id>.json`.
- The manifest uses these fields: `id`, `name`, `author`, `description`, `version`, `has_pck`, `has_dll`, `dependencies`, `min_game_version`, and `affects_gameplay`.
- For a DLL-only mod, set `has_dll` to `true` and `has_pck` to `false`.
- With no `ModInitializerAttribute`, the game creates a Harmony instance and runs `PatchAll` on the mod assembly.
- `ModInitializerAttribute` can be used when explicit static initialization is needed.
- Steam Workshop mods are loaded by the same ModManager, but local development can use the game's `mods` directory.

## Useful References

- Reference assemblies: `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll` in `data_sts2_windows_x86_64`.
- The game's XML documentation is `sts2.xml` beside `sts2.dll`.
- Public tutorial repository: https://github.com/GlitchedReme/SlayTheSpire2ModdingTutorials
- Public tutorial site: http://tutorials.sts2modding.com/

## Smith Audio

- Rest-site Smith uses `MegaCrit.Sts2.Core.Nodes.Vfx.NCardSmithVfx`.
- Its private animation methods call `NDebugAudioManager.Play("card_smith.mp3", ...)`. These are async methods; the actual sound call lives in compiler-generated state-machine and callback methods, not necessarily in the outer `PlayAnimation` IL.
- There are two relevant overloads: a parameterless animation and an `IEnumerable<CardModel>` animation.
- `NCardSmithVfx` is also used for the visual Smith effect. Keep the original animation and replace only the string passed to the audio call so timing stays aligned with the hammer effect.
- `NDebugAudioManager.Play` loads temporary audio from `res://debug_audio`. Embedded MP3 audio can be loaded with `AudioStreamMP3.LoadFromBuffer(byte[])`.
- The custom sound can be embedded as a managed resource. The built-in audio file is then not required beside the DLL.

## Compatibility Rules

- Do not globally replace the literal `card_smith.mp3` in `NDebugAudioManager.Play`. Other mods can use that manager or the same sound name for unrelated behavior.
- The current narrow patch checks `card_smith.mp3`, an active rest site, and that the call stack originates from `NCardSmithVfx` or one of its generated nested types. This preserves the game's original callback timing and leaves unrelated callers alone.
- Keep the replacement prefix restricted to an active `NRestSiteRoom` so the custom sound cannot affect other contexts.
- Create the replacement `AudioStreamPlayer` under the existing debug audio manager, use the `SFX` bus, preserve the original volume, and free the player on `Finished`.
- After a game update, re-check the method names, overload signatures, and original sound string with ILSpy. Internal game APIs are version-sensitive.
- Test with other audio-heavy mods enabled, especially HextechRunes, because broad Harmony patches can interfere with their audio and visual hooks.

## Build and Install

- `global.json` pins the SDK used by this project.
- Build with `dotnet build BingSmith/BingSmith.csproj -c Release`.
- The release folder should contain only the DLL and matching JSON manifest. The OGG is embedded in the DLL.
- Install the release folder under `<game>\mods\BingSmith\`, then enable the mod in the game's Mod menu.
- BingSmith's settings page uses RitsuLib Settings 0.6.3. Its manifest declares `STS2-RitsuLib` as a dependency. `ModInitializer` registers the settings page and must call Harmony `PatchAll` explicitly.
- A callback binding exposes the enabled state and volume to RitsuLib; values persist in `%APPDATA%\SlayTheSpire2\BingSmith\settings.json`.
