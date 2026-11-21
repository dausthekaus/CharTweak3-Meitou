# CharTweak 3 for MeitouClient

C# code mod for the CharTweak2 feature port. **0.4.0-beta.4** adds optional uniform
assignment to all selected squad members and selection-count notes in both profiles.
The color window includes clear mode choices, a character profile and optional RGB controls.
The main menu includes a selected-character profile and a hide/show shortcut for the launcher. Custom portraits support
Linux/Windows file selection and images embedded in saves. It also includes personal clothing
colors, a shared uniform with per-character membership, and named palette presets.
The personality selectors (gameplay and new-game creation) and selected-player
character editor button remain available in both HUD modes.
The feature inventory and porting rules are in [docs/migration.md](docs/migration.md).

## Character profile and launcher visibility

Open **CharTweak 3** to see the selected character's portrait, name, current
personality and two color swatches with hex values. Custom portraits appear here
too; otherwise the portrait shows the actual character's current appearance.
The profile follows the same selected/inspected character as the game's HUD and
updates while paused. Its personality and colors describe applied state, so
previewing a choice does not change the profile until that edit applies.
When several player characters are selected, both the main and color-window
profiles show **(N selected)** beside the displayed character's name.

Color labels distinguish **Personal**, **Uniform** and **Equipment**. Equipment
shows the first dyeable item's native color pair; other equipment can have other
dyes. **Equipment (no dye)** and **Unavailable** show neutral gray swatches instead
of suggesting that a default red/black palette is applied. The compact layout
retains the portrait, name, personality and swatches on small viewports.

Press **backslash (`\`)** to hide/show only the upper-left launcher button. The
main window's **Hide button (\) / Show button (\)** control does the same and keeps the
window open. The profile includes a reminder that backslash restores the launcher.
You can also recover the launcher through
**Settings → Mods → CharTweak3-Meitou → Show CharTweak button (\)**, whose help text
explains the shortcut works with the menu closed. Visibility is
remembered in `interface.json` beside `palettes.json`, across games and restarts.

On clients exposing `ModContext.AddKeyAction`, **Show/hide CharTweak button** is a
normal rebindable Controls action with backslash as its default. **Meitou 0.22.1**
predates that API: it uses a managed backslash compatibility shortcut instead,
ignoring text fields, the title and keys already assigned to game actions.
F11 keeps its existing frame-statistics binding. These controls use Meitou's
managed input/settings APIs on Linux and Windows; there is no OS key polling.

## Personality selector

### During new-game character creation

Use the **Personality** arrows near the bottom of the space between the editor's
side panels. **Default (automatic)** keeps the game's normal personality behavior;
the other choices select one of the ten named personalities below.

Use the editor's **Change Character** arrows to set each starting character
independently. Choices remain drafts until the native **CONFIRM** button is
accepted, including its warning about unvisited characters. They apply on the
first gameplay tick and persist through normal saves. Closing the editor without
confirming discards the drafts. Animals and non-player characters cannot use
this creation picker.

### During gameplay

1. Select a character and click **CharTweak 3** near the upper-left corner.
2. Check the displayed name and current personality. When inspecting an NPC,
   the selector follows that NPC, like the game's HUD. With multiple characters
   selected, it edits only the one named in the panel.
3. Use **<** and **>** to preview a personality, then press **Apply**.
4. If paused, unpause. Wait for **Personality updated.** before saving normally.

Drag the main CharTweak/personality menu by its title strip to move it aside. It
keeps its moved position when closed and reopened in the same interface.

Choices: Honorable, Traitorous, Smart, Dumb, Brave, Fearful, Warm / Kind,
Cold / Cruel, Normal and Maniacal. Changing the selected character resets the
preview. No selection or a dead character disables editing. Close dismisses
the panel; an already applied/queued edit remains requested.

Edited tags affect Meitou's dialogue personality checks. The beta stores them
in the character's native `personality` save field, which 0.22.1 reads but does
not normally update. Scoped Harmony read/write hooks preserve explicit values
across later saves; naturally derived values and dialogue locks keep their
engine behavior. No external character-id database or sidecar is needed.

## Character editor

Select a living player character, open **CharTweak 3**, and click
**Character Editor**. The native editor opens for only the character named
in the panel; the CharTweak panel closes. Use the editor's **CONFIRM** button to
apply edits and return to the game, then save normally.

This uses the engine's free in-play editor mode, which keeps race and gender.
The button is disabled for NPCs, dead characters, missing selection and characters
without an available appearance. A failed editor opening leaves CharTweak open
with a status message. The current character and world are checked again on click.

Targets standard **MeitouClient 0.22.1+**, code-mod API 2, **.NET 11**. Build against
the official 0.22.1 assemblies even when playing on a newer release. Later clients
may change engine APIs; the open manifest range is not a compatibility guarantee.
Legacy .NET 10 clients require a separate build.

## Clothing colors and uniform presets

Select a living player character, open **CharTweak 3 → Clothing Colors**.
The two-column color window keeps the selected character's portrait, name,
personality and currently applied colors at the top. The large color squares
show your draft; click either square to cycle through the eleven quick colors
from CharTweak2. Each square displays the color's name, or **Custom** for other
values, with contrasting text. Hex fields (`RRGGBB` or `#RRGGBB`) stay visible;
turn **Advanced RGB controls** on to show the six RGB sliders (0–255).
Drag the window by its title strip to move it aside. Its position is retained
when reopening it in the same interface. Toggling Advanced keeps your draft and
window position. Previewing changes only the large swatches; the profile keeps
showing applied colors. **Apply** and **Restore original**
keep the window open. Colors update at the next safe interface boundary even
while paused, without advancing game time; wait for **Colors applied.** or
**Original colors restored.** before saving. Use **Close** or Escape when finished.
Closing discards any unapplied draft and retains changes already applied.
**Back** does the same and returns to the main CharTweak menu.

| Mode | What Apply does |
| --- | --- |
| **Only this character (Personal)** | Sets this character's primary/secondary palette and leaves any uniform membership. |
| **Share a uniform (Uniform)** | Enrolls this character and updates the shared uniform colors for every enrolled character in this save. |
| **Restore original** | Removes this character's override and uniform membership, restoring its current equipment's native colors. |

To join the existing uniform, select another character, open Clothing Colors,
choose **Share a uniform**, then **Apply shared uniform** without editing its colors. Unenrolled
characters retain their own colors. Uniforms are per save; they do not recolor the
whole player faction automatically.

To enroll several characters at once, select them in the game, open Clothing
Colors, choose **Share a uniform**, and check **Include all selected squad members**
before pressing **Apply shared uniform**. The checkbox starts unchecked every
time you open the window and clears when you switch to Personal. It uses the
current selection at Apply, including selections across squads; living player
characters with a humanoid appearance are eligible. NPCs, animals, dead characters
and characters without an appearance are excluded. Existing uniform members
still receive shared color updates, including members outside the selection.
**Only this character** and **Restore original** keep their single-character behavior.
Checking the box does not apply any changes by itself.

For named palettes, enter a **Preset name** and click **Save new**. The selector
arrows choose a saved palette; **Load** copies it into the draft in either mode.
Use **Overwrite selected** to replace it. **Delete selected** requires a second
press. Saving or loading a preset does not change anyone until Apply is pressed.
Preset names are 1–32 characters and at most 64 palettes can be saved.

Character colors follow the wearer when equipment changes. Original item dyes,
shared faction/template records, skin and hair stay intact. Only dyeable clothing,
backpacks and compatible prosthetic materials can show these colors; missing dye
maps and `dont colorise` still prevent recoloring. Animals and NPCs cannot use
this color window. The native appearance editor handles hair and skin separately.

Preferences and membership are saved with native character records. The active
uniform is also stored through Meitou's mod save-data API. Named presets are
shared across games in `$XDG_CONFIG_HOME/chartweak3-meitou/palettes.json`, normally
`~/.config/chartweak3-meitou/palettes.json`. Updating the mod preserves that file.
An unreadable preset file is reported and preserved; personal/uniform editing
still works. Legacy C++ palette files are not imported automatically.

See [the implementation notes](docs/character-colours.md) for persistence,
appearance isolation and the remaining in-game visual check.

## Custom portraits

Select a living player character, then **CharTweak 3 → Choose Portrait**. Choose
an image in the OS file chooser; PNG, JPEG, BMP and TGA are offered. Wait for
**Portrait applied.**, then save normally. **Reset Portrait** restores the game's
rendered portrait for that character. Both actions keep the draggable menu open
and apply while paused. Changing the selected character/world during selection
discards the result. Cancelling or an invalid image keeps the previous portrait.

Linux uses **kdialog** on KDE and **zenity** on other desktops, with fallback when
the preferred tool is unavailable. Install either if missing (`sudo dnf install
zenity` on Fedora). Windows uses its native Unicode Open dialog without requiring
PowerShell, Python, WinForms or external tools. The same mod DLL runs on both OSes.
The game keeps its existing pause state while the chooser is open.

Images are copied into the `portraits/` directory beside `palettes.json` (normally
`~/.config/chartweak3-meitou/portraits/` on Linux and
`%APPDATA%\chartweak3-meitou\portraits\` on Windows). Imports preserve aspect ratio
and transparency, resize to at most 512 pixels per side, and leave the source
unchanged. The source limit is 16 MiB, 8192 pixels per side and 16 megapixels;
up to 128 different images can be assigned in one world.

Each assignment lives in its character's save record, and the image bytes are
embedded once per image in Meitou's `meitou-mods/chartweak3-meitou.dat` save file.
Copy the **whole save folder** when moving saves between Linux and Windows; no
original image paths are required. Saves grow with the number of distinct images.
Imports that omit Meitou mod data can use the local portrait cache when available.
Legacy CharTweak2 portrait assignments are not imported automatically.

Custom portraits replace live-character images in the squad bar, squad windows
and conversation portraits while preserving native frames, names, health/status
overlays and selection actions. Non-square pictures fit inside the portrait box
without cropping or stretching. Template/record portraits remain native.

Build and headless integration checks cover Linux chooser process routing,
both HUD modes and full disk save/load with the original image and cache removed.
The user has confirmed the chooser, portraits and save persistence in-game on
Linux. The Windows dialog ABI is checked on Linux; opening the actual Windows
dialog still needs a Windows test. The revised color layout needs an in-game visual check.
See [portrait implementation notes](docs/custom-portraits.md).

## Build on Fedora/Linux

Install a .NET 11 SDK (`dotnet --list-sdks` must show 11.x), Bash, tar and
coreutils. The installed Fedora 43 SDK `11.0.100-rc.3.26522.148` also builds this
scaffold; a stable .NET 11 SDK is preferred for ongoing development. No Wine,
Visual Studio, KenshiLib, RE_Kenshi or separate Harmony package is needed.

Extract the [official standard Linux 0.22.1 release](https://github.com/VlcekM/MeitouClient/releases/tag/v0.22.1).
Use the directory containing `MeitouClient.dll`, `Meitou.Simulation.dll`,
`Meitou.Data.dll`, `Meitou.Core.dll`, `0Harmony.dll` and `StbImageSharp.dll`:

```sh
dotnet build CharTweak3-Meitou.csproj -c Release -p:MeitouDirectory="/path/to/Meitou-0.22.1"
./scripts/package.sh "/path/to/Meitou-0.22.1"
```

For repeat builds, copy `Meitou.local.props.example` to the ignored
`Meitou.local.props` and set the path there. Alternatively, keep baseline DLLs in
`artifacts/meitou/0.22.1/`, the default reference directory. Then run:

```sh
dotnet build -c Release
./scripts/package.sh
```

No framework checkout is compiled or modified. References use `Private="false"`;
the host supplies them. The build rejects missing DLLs and a different Meitou
assembly version so the DLL cannot silently require more than its manifest says.
The documented release-assembly reference approach also works offline once the
SDK is installed; this project has no NuGet package dependencies.

## Deploy

The package is `artifacts/CharTweak3-Meitou.tar.gz`. With MeitouClient closed:

```sh
./scripts/deploy.sh "/path/to/Kenshi"
# Or: KENSHI_PATH="/path/to/Kenshi" ./scripts/deploy.sh
```

Current local installation:

```sh
./scripts/deploy.sh "$HOME/.local/share/Steam/steamapps/common/Kenshi"
```

This installs into `<Kenshi>/mods/CharTweak3-Meitou/`, preserves other files in
that folder, and leaves the load order to the launcher. Enable **CharTweak3-Meitou**
in its Mods list and restart. Look for the initialization message in `log.txt`.
Rebuild and package before deploying source changes; there is no hot reload.

For manual deployment, extract the archive into `<Kenshi>/mods/`. The loader needs
these three files together, with matching case on Linux:

```text
mods/CharTweak3-Meitou/
  CharTweak3-Meitou.mod
  meitou.json
  CharTweak3-Meitou.dll
```

The archive also includes this README and the existing MIT license. The `.mod`
is a valid V17 FCS file with no records, generated with Meitou's `FcsWriter` for
launcher discovery. Keep the manifest id `chartweak3-meitou` stable once used in
saves. Mod version is declared in both the project and manifest; update both.

## Framework references

Follows the official [code-mod guide](https://github.com/VlcekM/MeitouClient/wiki/Writing-code-mods),
[loader contract](https://github.com/VlcekM/MeitouClient/blob/4edc8a4c67712e64151e5de6175e404dfc9791c7/docs/modding.md)
and [manifest/version rules](https://github.com/VlcekM/MeitouClient/blob/4edc8a4c67712e64151e5de6175e404dfc9791c7/docs/modding-dependencies.md).
The entry point implements `Meitou.Modding.IMod.Initialize(ModContext)` and
registers an `IUiExtension` and a per-world personality system. Controls, styles
and actions are namespaced in `UiSlots.Overlay`; each UI registration owns its
panel state. Empty space passes clicks through to the game.

`PlayerInterface.BindUi` supplies the exact HUD character and a serially
published personality view. A paused-interface hook refreshes selection views;
edits are custom `SimCommand` values applied in `Inputs` at the next tick.
Commands capture the world, character slot and generation, so an old panel
cannot edit a reused slot or a new world's character. Explicit saved/edited
personalities enter the mod system's state hash. Hooks check their target
signatures during initialization. No framework source changes are required.

The editor button calls the existing `CharacterEditorPanel.OpenEditor` entry
point with `EditKind.Recruit` and one character. The engine owns its draft,
preview, camera, pause and confirmation commands; the mod does not replace them.

Creation uses scoped hooks on `CharacterEditorPanel.AddEditorContent`,
`EditorAction` and `ConfirmEditor`, since normal HUD extension slots are hidden
during editing. Controls participate in the editor's modal pointer routing and
keyboard navigation. Drafts belong to one editor opening and world; actions
also carry the character slot/generation and opening token. Confirm queues the
same personality command used in gameplay. No personality override is queued
for Default, cancellation or an unaccepted confirmation warning.

Colors use a separate per-world `ColourSystem`. A guarded `SetColours` command
updates character preferences in Inputs; SlowWorld copies and tints an appearance
only when its source or palette changes. Separate part/body-layer collections
preserve shared appearances, original item dyes and body metadata. The renderer
receives private immutable COLOR_DATA records, never global database edits.
The native hidden-hat rebuild retains the tint. Namespaced character save fields
hold RGB values and membership; `IModSaveData` holds the current uniform without
transient character IDs. `PaletteLibrary` atomically saves named presets.

The Clothing Colors screen uses native MyGUI edit boxes and sliders routed through
scoped `ScreenRouter` hooks. Modal input, focus, viewport fitting and pausing remain
the engine's. All hook signatures/collection types are validated on initialization.
An isolated paused-interface postfix takes only `SetColours` commands, updates
cold visual preferences and publishes a paused snapshot. Other custom commands
keep their normal tick behavior. A transparent native title strip supplies dragging
for the main HUD panel; it is hidden when the panel or HUD is hidden.

The 0.22.1 binary does not contain the lasting-id API in newer source docs;
this beta uses the release's actual save reader/writer. Recheck the Harmony
targets and save behavior when changing the supported baseline.

## Verification

Build the mod, then run the integration checks against the standard Linux
release (Python 3 is also required):

```sh
./scripts/check.sh "/path/to/Meitou-0.22.1"
```

Checks use synthetic characters and the real `ModHost`, HUD bindings, `UiHost`,
dialogue personality accessor, `SaveCapture` and `SaveLoader`. They exercise both
HUD modes, all ten tags, preview/apply, paused selection and queued edits,
NPC inspection, no selection, save files on disk, repeated saves, dialogue locks,
invalid/stale commands, state hashing and character isolation. Editor checks
open and confirm the real native editor, verify single-character targeting and
in-play mode, reject stale selections/NPCs, and check failed-opening feedback.
Creation checks exercise native modal clicks, independent squad drafts, default
and cancellation, stale frames from another character/opening, the native warning,
confirmation, the first tick and personality save fields. A separate color probe
checks individual mesh/shirt dyes and binary inventory persistence on identical
equipment in the same faction.

Personal/uniform checks also drive the real native color screen in both modes,
including slider clicks, typed hex, invalid input, presets, cancellation and stale
modal actions. They also verify both draggable panels, remembered positions,
repeated Apply/Reset with the color window open, paused renderer snapshots without
time advancement, and unrelated commands remaining queued. They verify same-gear isolation, native character edits and hide-hat
rebuilds, reset, native color saves on disk, loaded uniform membership, mod sidecars,
import fallback, invalid/dead/reused targets and deterministic hashes across thread
counts. Preset checks cover disk reload, overwrite/delete, corrupt input and write
failures. Portrait checks cover both HUD modes, asynchronous chooser routing,
selection changes, cancel/reset, image normalization, native atlas replacement,
and full disk saves restoring embedded images with the source and cache removed.
The checks use the
release's bundled .NET runtime so Harmony runs even when Fedora's SDK is an RC.
Only temporary test files are written; no Kenshi assets or existing saves are used.
The script sets `CHARTWEAK3_CONFIG_HOME` to its temporary directory to isolate
presets and portrait copies from the player's library.

The Release build and checks pass on Fedora 43 against official 0.22.1. The
archive contains only the mod's DLL, data file, manifest, README and license.
The user has confirmed the gameplay personality selector, clothing colors,
uniforms, draggable menus, open-window Apply/Reset and persistence work in-game.
Custom portrait presentation and the Linux OS chooser have also been confirmed.
The new profile layout and launcher shortcut need an in-game check; integration
checks run without a GPU. The Windows dialog requires a native Windows test.
See the manual portrait checks in
[docs/custom-portraits.md](docs/custom-portraits.md#validation-and-remaining-manual-checks).
