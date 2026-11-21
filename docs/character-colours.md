# Character colors and uniform implementation

Implemented in **0.3.0-beta.3**, targeting the actual MeitouClient 0.22.1 release
assemblies. The [README](../README.md#clothing-colors-and-uniform-presets) describes
the controls and the Personal / Uniform / Reset behavior.

The **0.4.0-beta.3** UI pass keeps the two color columns and existing behavior.
The selected-character profile shows the applied portrait, name, personality
and colors above the draft controls. Mode buttons read **Only this character**
and **Share a uniform**, with explicit explanations of leaving/joining the
uniform and updating its existing members. Quick-color names appear inside
the clickable swatches with contrasting text; redundant quick-color arrow rows
are removed. Saved named palettes remain. RGB sliders start collapsed behind
**Advanced RGB controls**, while hex fields stay visible. Replacing the native
layout on toggle preserves text, slider state and the dragged top-left position;
the toggle never applies colors. Color application, hooks and save formats are
unchanged by this UI pass.

**0.4.0-beta.4** adds an unchecked **Include all selected squad members** checkbox
in Uniform mode. Apply sends the same existing `SetColours` Uniform command to
each eligible character in the current published selection, deduplicating the
displayed character. The simulation retains all existing validation, uniform
membership and color application behavior. Personal and Restore original remain
single-character actions. Switching to Personal or reopening the editor clears
the opt-in. Both profiles append the selected player-character count to the name
when more than one is selected. There are no new save fields or formats.

## Rendering and character isolation

`ColourAppearance` makes a shallow copy of the complete `CharacterAppearance`,
then replaces its Parts and BodyLayers collections with separate lists. Eligible
parts/layers use record copies with a private immutable COLOR_DATA record holding
the requested primary and secondary RGB24 values. Other body, pose, light,
attachment, hidden-part and race metadata is retained. Source collections and
records are never mutated. The two backing-field types and hidden-hat method are
checked during initialization against the installed release.

The dye record is built in a temporary private GameDatabase; it is never applied
to the game's merged database or written into inventory items. Its ID is not a
persistent character identity. The renderer reads the record directly through
CharacterPart.Colour and BodyLayer.Colour. Its per-appearance asset cache sees a
new appearance object when a palette changes, so the material is rebuilt without
OGRE material detours or a shared faction color override.

The world system retains the latest original appearance. It creates a tinted
copy only after a source appearance or palette change. Normal ticks reuse the
same copy. Native equipment changes and character edits that supply a fresh
appearance are picked up in SlowWorld. Reset restores the latest source and
removes the preference; native item dyes are preserved for trading, looting and
reset. A scoped postfix preserves the preference when WithoutHat rebuilds a
render-only appearance. The native character-editor draft preview can show the
base clothing; its confirmed appearance regains the palette on the next tick.

ARMOUR and CONTAINER records must allow coloring (`dont colorise` false).
Compatible prosthetic materials can use the same palette. The native material
must also supply a color map, or the assigned dye has no visible effect. Native
paint factors and lighting remain in effect. Hair, skin, weapons, faction
records and character templates are not modified. Animals and NPCs cannot use
the color window.

## Threading, commands and validation

A separate ColourSystem is registered at SystemAnchor.End for each world. UI
bindings contain only a serially published selected-character view. The native
Clothing Colors screen uses real MyGUI edit boxes and byte-range sliders,
registered lazily in MyGuiScreenHost and routed by validated ScreenRouter hooks.
The movable Window root uses native title-strip dragging and retains the screen's
offset across openings. Primary/secondary swatch buttons cycle the same eleven
quick colors, updating only the draft. Their hover/pressed fill
retains the actual preview color.
It shares the native modal input, keyboard focus, viewport fitting and pause
handling. The host requires template-relative geometry for slider parts; the
layout supplies TemplateRealPosition explicitly.

RGB/hex and preset edits change only the window draft and swatches. Apply and
Reset revalidate the displayed character, keep the modal open, and enqueue SetColours.
Commands carry a world token plus character slot/generation and validate mode,
RGB range, character existence, player ownership, appearance and life state in
Inputs. SlowWorld applies appearance changes after native systems finish.
While paused, ColourPausedUpdates takes only SetColours commands after the native
ApplyPausedInterfaceCommands boundary, applies the same validation and color
refresh, and calls the validated native PublishPaused entry point. These changes
touch only cold visual state; no simulation time or hot character buffers advance.
Other custom commands stay queued for their normal tick. The window acknowledges
the published color view, and Reset refreshes its fields from the native colors.
Close/Escape discards only unapplied edits.

MainPanelDrag provides a transparent native Window title strip for the main
UiHost personality panel. Native pointer capture and screen offsets handle the
drag. The whole panel follows that offset and stays inside the viewport. The
strip closes with the panel, when opening an editor, or while the HUD is hidden;
reopening the menu keeps the offset. The color window retains its separate native
dragging. Both controls remain scoped to their own interface.

An opening-specific nonce rejects actions from a closed/reopened modal. Original
personality controls and new-game creation controls keep their own behavior.
Color preferences, uniform membership and active uniform RGB enter the system's
state hash; transient world tokens and cached appearance objects do not.

## Persistence

Each character preference is held under a weak CharacterCold key. Scoped postfixes
on DialogueSave.Read and Write use these GAMESTATE_CHARACTER int fields:

- chartweak3.colour.version = 1
- chartweak3.colour.mode = 1 (Personal) or 2 (Uniform)
- chartweak3.colour.primary = RGB24
- chartweak3.colour.secondary = RGB24

These records follow characters through native saves and squad moves without
persisting transient CharacterId values. Reset removes all four fields, including
from a reused save record. Malformed versions, modes and colors are ignored on
read. Ordinary personality fields, dialogue locks and item colors remain native.

The active uniform is per world, with one shared palette and explicit membership.
ColourSystem implements IModSaveData, registered as `colours`; its versioned JSON
contains only the active flag and RGB values. Meitou owns the mod sidecar. Member
records also store their effective colors; when an import omits the sidecar, the
first loaded member's saved palette initializes the uniform. New games start with
the legacy C++ default (180,45,35 / 35,35,35), with no characters enrolled.

Named palettes are user-wide JSON in
`$XDG_CONFIG_HOME/chartweak3-meitou/palettes.json` (normally ~/.config). The test
script overrides the config root with CHARTWEAK3_CONFIG_HOME under a temporary
directory. Palette names are JSON data rather than paths. Up to 64 palettes are
accepted, names are 1–32 trimmed characters, duplicates are case-insensitive, and
RGB values must be within 0x000000..0xFFFFFF. Save-new refuses duplicates;
overwrite and delete are explicit actions. Delete requires two presses in the UI.

Writes use a temporary file in the same directory and an atomic replacement.
Failed writes leave the in-memory library unchanged. An unreadable, oversized or
invalid preset document is reported and preserved, with library writes disabled
for that session; character color controls still work. Legacy C++ palette and
membership sidecars are not automatically imported.

## Verification and remaining visual check

The release-runtime integration checks use the real ModHost, native UI router,
text buffer, slider hit testing, WorldSession, SaveCapture/SaveLoader and mod
sidecar capture/restore. They verify:

- Personal colors on identical shared source appearances, separate mesh and
  shirt-overlay colors, unchanged source collections and inventory dyes.
- Native slider clicks, typed hex, validation, swatches, presets and stale modal
  rejection in both HUD modes; the native window fits an 800x600 viewport.
- Main-menu and color-window dragging, retained positions, repeated Apply/Reset
  without closing, paused renderer snapshots with no tick advancement, and
  non-color custom commands remaining queued.
- Applied profiles in the color screen, quick-color names and contrast,
  mode explanations, Advanced toggles preserving valid/invalid drafts, and
  both collapsed and expanded layouts fitting an 800x600 viewport.
- Uniform enrollment/update/leave, nonmember isolation, original-color reset,
  stable cached looks, native EditCharacter commands and hidden-hat rebuilds.
- Optional bulk uniform enrollment across selected squads, unchecked defaults,
  current-selection targeting, exclusion of animals/missing appearances, and
  unchanged single-character Personal/Restore behavior. Selection notes remain
  visible in both color and main profiles, including the compact main layout.
- Native saves written to disk and read back, independent personal preferences,
  loaded uniform membership, repeated saves and import without the sidecar.
- Invalid modes/RGB, NPC/dead/reused-slot/stale-world targets and state hashes
  consistent with one and four simulation workers.
- Palette disk reload, duplicate/overwrite/delete, Unicode/path-like names,
  malformed JSON and failed-write preservation.

The earlier ColourChecks probe also verifies native per-item dye persistence and
faction isolation. Checks use synthetic assets without a GPU. The user has
confirmed visible colors, persistence, open-window apply/reset and dragging
in-game. The revised color layout still needs an in-game visual check.
For regression testing, compare two characters wearing identical
dyeable clothing, try both mesh armor and shirt overlays, then test uniform
updates, leaving/reset, equipment exchange, native editor confirmation, hide hats
and save/reload. Custom portraits are described in [their own notes](custom-portraits.md).
