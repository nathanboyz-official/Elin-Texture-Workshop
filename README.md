# Elin Texture Manager

A Windows desktop application for managing texture replacement mods for the Steam game
**Elin**. It scans your installed Workshop mods, shows every replacement texture visually,
finds the ones supplied by more than one mod, and lets you pick exactly which version you
want — per texture, not per mod.

It is **not** a load-order editor. Load order is included, but the primary system is a
per-texture override manager.

---

## What it does

You subscribe to texture mods on the Steam Workshop. Several of them replace the same
character. Normally you would have to guess which Workshop folder holds which texture and
shuffle mod order until the right one wins.

Instead:

1. Open Elin Texture Manager. It finds Elin and your Workshop mods on its own.
2. Click **Conflicts** to see only the textures more than one mod supplies.
3. Click a texture. Every installed version appears side by side, as images.
4. Press **USE THIS TEXTURE** on the one you want.
5. Restart Elin. That exact texture is in the game.

The chosen texture is copied into a dedicated local mod. Your Workshop folders are never
written to.

---

## Features

**Texture browser**
- Grid of every replacement texture, grouped by category (Characters, Items, Portraits,
  Objects, and any other prefixes found)
- Transparency shown against a checkerboard, nearest-neighbour scaling for pixel art,
  aspect ratio always preserved, sprite sheets shown whole
- Conflict badges, override badges, and the mod that currently wins
- Virtualised grid, so thousands of textures stay smooth

**Comparison view**
- The current active texture large, with a stated confidence level
- Every installed version as a card: preview, mod name, Workshop ID, resolution, file
  size, SHA-256
- Pick any two and compare them side by side, with an A/B toggle for subtle differences
- Byte-identical versions are labelled as such, so you do not hunt for a difference that
  is not there

**Per-texture overrides**
- Selected textures are copied into `Elin\Package\ElinTextureManager_Overrides`
- Remove one override and Elin falls back to its normal load-order winner
- Detects when Steam updates a mod you selected from, and asks before changing anything
- Detects when you unsubscribe from a source mod, and keeps your override
- Import and export your selections as JSON, or export the whole thing as a standalone mod

**Mods, load order and conflicts**
- Every detected mod with texture count, conflict count, unique count and load position
- Click a mod to browse only its textures
- Visual load-order editor with drag-and-drop, enable/disable, and a mandatory backup
  before any write

**Everything else**
- Watches the Workshop folder and notices new, updated and removed mods (debounced, so a
  Steam download does not trigger a rescan storm)
- SQLite cache of texture metadata, so repeat launches do not re-hash every PNG
- Search across texture IDs, numbers, mod names and Workshop IDs
- Optional per-texture aliases (`objC_2115` → "Gaki")
- Detects whether Elin is running and tells you to restart it, never touching the
  running game
- Dark UI, remembers window size, filters and thumbnail size

---

## Installation

Download or build `ElinTextureManager.exe` and run it. There is no installer, no account,
no network access, and no browser component.

Application data lives in `%APPDATA%\ElinTextureManager`:

```
settings.json      paths, options, window state
selections.json    your texture choices
aliases.json       custom texture names
cache.db           SQLite metadata cache
Logs\              application log
Backups\           load-order backups
```

Nothing is written inside your Steam Workshop folders.

---

## How Workshop scanning works

Elin's Steam App ID is **2135150**. The application:

1. Reads Steam's install path from the registry
   (`HKCU\Software\Valve\Steam`, falling back to `HKLM`).
2. Parses `steamapps\libraryfolders.vdf` to find **every** Steam library, including ones
   on other drives. Nothing is hardcoded to `C:`.
3. Looks for `steamapps\common\Elin` and
   `steamapps\workshop\content\2135150` in each library.
4. Walks every Workshop item looking for a folder named `Texture Replace`, reading
   `package.xml` for the mod's title, author, version and load priority.
5. Indexes every supported image inside those folders by **file name**, because that is
   how Elin matches a replacement texture.

If detection fails, you can browse for the Elin folder yourself in Settings; the choice is
remembered.

### Texture identity

A file name is parsed into an ID, a prefix and a number:

```
objC_2115.png   ->   ID objC_2115   prefix objC   number 2115
```

Names that do not fit that shape (`world.png`, `objs_S_snow.png`) still get an entry —
they keep their whole name as the ID. Unknown prefixes are never discarded; they appear
under **Other** and remain filterable.

### Variants

Some mods keep alternate sets in sub-folders of `Texture Replace`, for example `unused` or
`1_Regular_Tights`. Elin loads the files sitting **directly** in `Texture Replace`, so
those sub-folder files are shown as *variants*: selectable, but not counted as active
conflicts. Selecting one copies it into your override package, which does make it active.

---

## How the override system works

Global mod ordering cannot express an arbitrary per-texture choice. If Mod A has
`objC_100`, `objC_101`, `objC_102` and Mod B has `objC_100`, `objC_105`, no ordering gives
you `objC_100` from B and `objC_101` from A.

So the application maintains its own local mod:

```
Elin\Package\ElinTextureManager_Overrides\
├── package.xml
└── Texture Replace\
    ├── objC_2115.png
    ├── objC_1532.png
    └── ...
```

Selecting `objC_2115` from Mod B copies Mod B's `objC_2115.png` into that folder. The
original is opened read-only and left byte-for-byte identical.

`package.xml` follows the schema used by the packages shipped with the game
(`Package\_Elona`, `Package\_ModdingKit`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Meta>
  <title>Elin Texture Manager Overrides</title>
  <id>elintexturemanager.overrides</id>
  <author>Elin Texture Manager</author>
  <builtin>false</builtin>
  <loadPriority>1000</loadPriority>
  <version>1.0.0</version>
  <description>...</description>
</Meta>
```

Elin's own packages use negative load priorities (Elin Core is `-100`, the Modding Kit is
`-90`), so a large positive value places this package last.

### Removing an override

**REMOVE OVERRIDE** deletes only the copy inside the override package. The source mod is
untouched and Elin falls back to its normal load-order winner.

---

## Why Workshop files are never modified

Steam owns those folders. It will re-download and overwrite them whenever a mod updates or
you verify your files, so any edit made there is temporary at best and destructive at
worst. The application therefore treats them as strictly read-only, and every delete is
checked before it happens. A delete must prove that the path:

1. exists and is a file, not a directory,
2. resolves underneath the configured override texture folder,
3. matches the expected file name for that selection,
4. is not inside the Workshop folder,
5. is not inside `Elin_Data`.

If any check fails the operation is refused and logged. Deletions are never recursive.

---

## How load order backups work

Elin stores its load order at `Elin\loadorder.txt`, one line per Workshop mod:

```
C:\...\steamapps\workshop\content\2135150\3427330411,1
```

The trailing field is `1` for enabled and `0` for disabled. Local packages under
`Elin\Package` are **not** listed there, which is why the override package relies on
`loadPriority` instead of a load-order entry.

Before the file is written, a timestamped copy is placed in
`%APPDATA%\ElinTextureManager\Backups`:

```
loadorder.backup_2026-08-31_201500.txt
```

If the backup fails, the save is refused. The write itself goes to a temporary file and is
then moved into place, so an interrupted save cannot truncate the original. Lines the
parser does not recognise are preserved verbatim rather than dropped. **Restore Backup**
brings back the most recent copy, backing up the current file first so a restore is itself
reversible.

### A note on priority

`loadorder.txt` records the order of your mods but does not state which end wins a file
conflict, and the game does not document it locally. The application defaults to
**later entries win**, which matches the usual convention, and says so in the UI. You can
flip it in Settings. Where a winner cannot be determined — for example when some sources
are missing from `loadorder.txt` — the application says "uncertain" instead of guessing.
Your own overrides are unaffected by this setting.

---

## Building from source

Requires the .NET 8 SDK (or newer, targeting `net8.0-windows`).

```bash
dotnet build -c Release
```

Run the tests:

```bash
dotnet test
```

Produce a self-contained build that runs without .NET installed:

```bash
dotnet publish src/ElinTextureManager.App -c Release -r win-x64 --self-contained true -o publish/win-x64
```

The result is `publish/win-x64/ElinTextureManager.exe`. Single-file publishing is
deliberately not used — it causes problems with WPF dependencies, and stability matters
more here than a tidy folder.

### Project layout

```
src/ElinTextureManager.Core/   detection, scanning, indexing, overrides,
                               load order, cache, watching  (no UI dependency)
src/ElinTextureManager.App/    WPF application, MVVM
tests/ElinTextureManager.Tests/ unit tests
```

`Core` has no reference to WPF, so all the logic that touches your game folder is testable
on its own.

---

## Troubleshooting

**Elin was not found**
Settings → Elin Installation → Change, and pick the folder containing `Elin.exe` and
`Elin_Data`. Or press **Re-detect Steam and Elin**.

**No textures found**
Check the Workshop path in Settings points at
`...\steamapps\workshop\content\2135150`. Mods without a `Texture Replace` folder do not
contribute textures; untick "Only mods with texture replacements" on the Mods page to see
everything that was detected.

**My selected texture did not appear in game**
Restart Elin — changes only take effect at launch. If it still does not appear, try
Settings → Advanced → *Also copy overrides into `Elin\User\Texture Replace`*. That is
Elin's own user-level replacement folder and acts as a fallback. Removing an override
removes both copies.

**The wrong mod is shown as the current winner**
Settings → Load Order Priority, and flip the convention. This changes only what is
displayed; it does not change your overrides.

**A texture shows "uncertain"**
Some of its sources are not listed in `loadorder.txt`, so their relative priority cannot be
determined from local data. Selecting a version removes the ambiguity entirely.

**Two versions look the same**
They probably are. Versions with identical SHA-256 hashes are labelled
**IDENTICAL TEXTURE**, and a texture whose every version matches is called out explicitly.

**A mod is malformed**
One bad `package.xml` or corrupt PNG never stops a scan. The mod is listed with what could
be read, the problem is logged, and scanning continues. Settings → **Open Logs**.

**Steam updated a mod I had selected from**
The Selected Overrides page flags it as **SOURCE UPDATED** and offers to keep your current
override, update to the new version, or compare them. Nothing is changed without asking.

**I unsubscribed from a mod I had selected from**
Your override still works — it is a copy. The page shows **SOURCE MOD NOT INSTALLED** so
you can keep it, remove it, or choose another version.

---

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| `Ctrl+F` | Focus the search box |
| `F5` | Refresh mods |
| `Escape` | Close the detail panel |

None of them are required to use the application.
