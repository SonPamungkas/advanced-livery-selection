# Advanced Livery Selection
Take full control of which liveries appear, on which faction, and on which aircraft — including custom/workshop skins.
<img width="2906" height="2905" alt="ALS" src="https://github.com/user-attachments/assets/9fc5d970-778f-4d06-8500-72ee9693db19" />

## Description

Nuclear Option locks each aircraft's livery list behind softcoded faction assignments and gives you no way to manage your custom/workshop skin collection from in-game. **Advanced Livery Selection (ALS)** rewrites that pipeline: every custom livery gets a faction assignment you control, custom liveries can be hidden from the spawn menu entirely, and an automatic "No Vanilla Skin" mode keeps every spawned unit dressed in your custom collection whenever possible.

## How it works

ALS patches `LoadoutSelector.GetLiveryOptions` (the function that builds an aircraft's livery dropdown) with a prefix/postfix pair: the prefix neutralizes the game's native faction filter so nothing is pre-excluded, and the postfix then applies ALS's own complete, authoritative decision for every livery — faction match, hidden state, all of it. This is the only way to both *loosen* restrictions the game baked in and *add* new ones of our own.

- **Built-in liveries** have no on-disk record the game exposes for editing, so ALS keeps its own per-aircraft override files under `LiveriesConfig/` doesn't really do anything for now.
- **Custom/workshop liveries** already have a real `meta.json` that the game reads natively — ALS edits that file directly through the same I/O the game itself uses (`ModLoader.WriteMetaData`), so your changes are exactly what a manual hand-edit would produce, and they survive without any override layer for faction. Hidden state for customs (which has no native equivalent) lives in ALS's own small sidecar file and is applied purely by filtering the in-game list — your real `meta.json` is never touched for that.

## Features
- **Per-livery faction assignment** — set any custom livery to Boscali, Primeva, or Agnostic (available to both), directly from an in-game window.
- **Custom/workshop skin pool control** — hide or show individual custom liveries from the spawn menu. (Built-in liveries can't be hidden — emptying an aircraft's livery pool crashes the game's loadout generator, so ALS only allows hiding skins from the always-present custom pool.)
- **Automatic "No Vanilla Skin" mode** — a persistent toggle. While it's on, ALS continuously scans every spawned aircraft: if it's wearing a built-in livery and a custom one is available for it, ALS switches it to a random custom skin; if it has no valid custom optio (e.g. you hid them all), ALS mechanically keeps it on / reverts it to a built-in livery — no manual intervention, fully self-correcting as your pool changes.
- **Native-first editing** — custom livery edits go straight to the real `meta.json` your workshop/app-data tools already read and write; nothing is shadowed or duplicated.
- **Live scanning** — buttons to rescan custom livery folders and to discover every base/mod skin currently loaded, so newly-added content shows up without a restart.

## Configuration

Open the ALS window in-game (default hotkey **Ctrl+L**, configurable below) to manage factions, visibility, and the No-Vanilla-Skin toggle directly. The plugin also exposes a BepInEx config file (`advance.livery.selection.cfg`) with:

- **UI Hotkey** — the keyboard shortcut that opens/closes the ALS window (default `Ctrl+L`).
- **Scan Folders** — comma-separated folder paths ALS scans for custom livery JSON files
  (defaults to `Documents/NuclearOption/Liveries`).
- **No Vanilla Skin** — enable/disable the automatic custom-skin enforcement described above.
  Off by default; toggle it from the in-game window or the config file.

Built-in livery overrides (faction assignments, since built-ins have no real `meta.json`) are stored as small per-aircraft JSON files under the plugin's own `LiveriesConfig/` folder — back this folder up if you want to keep your faction setup across reinstalls. Custom livery edits go straight to your existing workshop/app-data `meta.json` files, exactly as if you'd edited them by hand. (this is the best non-destructive and non-duplicative method i could find)
