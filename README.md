# Mod Menu for ENDLESS Legend 2

[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-db61a2?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/JavierOrtegaP)

*If you like my work and Mod Menu keeps your mods tidy, you can
[sponsor me on GitHub](https://github.com/sponsors/JavierOrtegaP). It's completely optional, but always appreciated,
and it keeps me making more mods. Thank you! ❤️*

One key for all your mods. **F7** opens a single window (F7 or Esc closes it):

- A **sidebar** listing the mods that have a page, ★ pinned ones first, then by name; it scrolls, so it works the
  same with 3 mods or 300.
- **All mods**: every mod BepInEx loaded, with its version and a one-line status (for mods that give one).
- The selected mod's **page**, for example [Population Planner](https://github.com/JavierOrtegaP/el2-population-planner)
  or [Auto Foundations](https://github.com/JavierOrtegaP/el2-auto-foundations).

The menu knows nothing about the mods themselves; any number of mods can add a page (see below). The window opens
centred, can be moved by its title bar and resized from its bottom-right corner, and remembers your pins and the last
page you opened. While it is open, Esc only closes the menu; the game doesn't also open its pause menu.

There is deliberately no search box: keys typed into it would also reach the game's own hotkeys (Space, letters).

## Install

1. Install **BepInEx 5** for Windows x64 (tested with
   [5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)): extract it into the game folder (the folder
   with `Endless Legend 2.exe`), so that `winhttp.dll` sits next to the game's exe. Start the game once.
2. Download `ModMenu-<version>.zip` from the [releases](../../releases) and extract it into the same folder; it ends up
   in `BepInEx/plugins/ModMenu/`.
3. In game, press **F7**.

If nothing happens, check `BepInEx/LogOutput.log` for `Mod Menu ... active`. To uninstall, delete
`BepInEx/plugins/ModMenu` (and, if you like, `BepInEx/config/el2.modmenu.cfg`); mods with a page then go back to
their own windows and keys.

## Options

Saved in `BepInEx/config/el2.modmenu.cfg`.

| Option | Default | |
| --- | --- | --- |
| Window / ToggleKey | F7 | Unity Input System key name. The game uses F1-F6 and F8-F11. |
| Window / Size | 0 | 0 = follows the screen resolution (2x at 4K); else a multiplier. |
| Window / Opacity | 1 | Menu background opacity. |
| Window / Width, Height | 900, 680 | Menu size at 1x (also set by dragging the corner). |
| Window / LastTab | | The page the menu opens on. |
| Window / Pinned | | Mods pinned to the top of the list (set with ★). |

## Adding a mod to the menu

No reference to this mod is needed: the menu looks for these public members on your plugin class (the one with
`[BepInPlugin]`) by name.

```csharp
// Required for a page: its content, drawn with GUILayout. Called from the menu's OnGUI while your page is shown.
public void ModMenuDraw() { ... }

// Optional: your name in the list (default: your plugin's name).
public string ModMenuTitle => "Foundations";

// Optional: one line under All mods, e.g. "On, 3 bought this turn".
public string ModMenuStatus => ...;
```

Inside `ModMenuDraw`:

- Draw with `GUILayout` only. Don't open a `GUI.Window`, call `GUI.DragWindow`, or add your own outer scroll view
  (the menu scrolls your page).
- Change your mod's state from `Update`, not in the middle of drawing (queue the click, apply it next frame), so
  the layout and repaint passes agree.
- Let `ExitGUIException` through. Any other exception switches your page to an error message until the menu is
  reopened; the menu keeps working.

To leave the key to the menu when it is installed, check once the game is running (BepInEx may load your mod before
the menu):

```csharp
bool menuInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("el2.modmenu");
```

Clicks and the mouse wheel over the menu don't reach the map.

## Compatibility

- Made for ENDLESS Legend 2 **1.0** (Steam build 25623753).
- Any BepInEx mod is listed under All mods; mods without a page simply have no entry in the sidebar.

## Building

Needs the .NET SDK (7 or newer) and the game installed: the project compiles against the game's own assemblies, which
are not part of this repository. Tell the build where the game is with a `local.props`
(`<Project><PropertyGroup><GameDir>D:\...\ENDLESS Legend 2</GameDir></PropertyGroup></Project>`), the `EL2_GAME_DIR`
environment variable, or `-p:GameDir=...` (default: Steam's standard install path). BepInEx's DLLs come from the game
folder if BepInEx is installed there, else from `.cache/bepinex/BepInEx/core`. `dotnet build -c Release` builds,
copies the DLL into the game (when BepInEx is installed; `-p:SkipDeploy=true` to skip) and writes
`dist/ModMenu-<version>.zip`.

## License

[MIT](LICENSE). Not affiliated with or endorsed by Amplitude Studios.
