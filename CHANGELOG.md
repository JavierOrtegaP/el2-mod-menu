# Changelog

## 1.0.1 — 2026-10-04

- No changes to the mod itself. The download now comes with the current README (a screenshot, how to support the
  project), and Mod Menu is also on [Nexus Mods](https://www.nexusmods.com/endlesslegend2/mods/8), updated with
  every release.

## 1.0.0 — 2026-10-04

First release, for ENDLESS Legend 2 1.0 (Steam build 25623753).

- One key (F7) opens a window: a sidebar with the mods that have a page (pinned with a star first, then by name;
  scrolls for any number of mods), an All mods page (every loaded mod, version, status), and the selected page.
- A mod gets a page by declaring `ModMenuDraw` on its plugin class; no reference to this mod needed.
- Opens centred; movable, resizable from its corner; remembers pins and the last page. Esc closes it without also
  opening the game's pause menu; clicks over it don't reach the map.
