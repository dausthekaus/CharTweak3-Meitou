# Custom portraits (0.4.0-beta.1)

## Import and UI

`PortraitActions` belongs to each UI registration. Choose captures the original
personality target and portrait system's world token, then runs the OS chooser
and import off the UI thread. Only the UI thread consumes completion and enqueues
`SetPortrait`. It rechecks the current frame's host, world, selected character,
player ownership, appearance and life state. The simulation validates the target
again. Closing the menu keeps an import in progress; switching targets before
completion discards it. The menu stays draggable and Apply/Reset never close it.

`PortraitPicker` uses one chooser at a time. Linux starts kdialog or Zenity with
`ProcessStartInfo.ArgumentList`, reads both output streams, treats exit 1 as
Cancel, and tries the other installed tool if launch fails. Shell characters
in image paths remain ordinary data. Windows uses `GetOpenFileNameW` on a
dedicated STA background thread, with a 32768-character unmanaged UTF-16 buffer,
file/path existence checks, and `OFN_NOCHANGEDIR`. Cancellation is distinguished
from `CommDlgExtendedError`. There is no WinForms or Windows-specific build target.

Native API references: [Windows dialog](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/nf-commdlg-getopenfilenamew),
[OPENFILENAMEW](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/ns-commdlg-openfilenamew),
[KDE file selection](https://develop.kde.org/docs/administration/kdialog/).

`PortraitFiles` checks source byte count and decoded dimensions with the host's
StbImageSharp before decoding. It fits large images to 512 pixels per side,
encodes an 8-bit RGBA PNG using BCL zlib, and names the copy with its SHA-256.
Identical normalized images share one file. The original is never modified.
Cache writes use temporary files and atomic replacement. Invalid images retain
the previous assignment. A cache write failure is logged and the UI rejects the
new assignment; restored embedded images remain in memory for later saves even
if a read-only cache prevents displaying them.

## Persistence and threading

`PortraitSave` attaches a content hash to `CharacterCold` via a weak table and
the native character's `chartweak3.portrait.v1` string. Hooks on
`DialogueSave.Read/Write` match the other CharTweak character fields and remove
stale fields on reset. Neither character names nor transient IDs identify saved
assignments. Two characters sharing a CHARACTER template can have different images.

`PortraitSystem` is instantiated per world. Commands apply in `Inputs` and publish
immutable views in serial `SlowWorld`; only these views are read by the renderer.
The existing paused visual-command boundary also drains `SetPortrait` without a
gameplay tick. Personality commands retain their existing tick behavior.
Image identities participate in state hashing; world GUIDs and OS paths do not.

The `portraits` entry of `IModSaveData` stores version 1, an image count, then
each assigned hash and length-prefixed PNG bytes using `BinaryWriter`. Each image
appears once, sorted by hash. The reader bounds sizes/counts, rejects duplicates,
verifies hashes/dimensions and decodes before accepting the bundle. It reconstructs
the local cache, so saves carry images across OSes even after deleting the source.
Normal reset omits unused images from subsequent bundles. Local copies are kept
for older saves and imports; the mod does not delete user storage automatically.

## Rendering

The actual 0.22.1 `PlayerInterface.ArrangeUi` postfix maps native portrait atlas
rectangles to `PortraitBar.PortraitMembers` and the two conversation character
cells. It copies matching ImageBox controls and their properties rather than
mutating cached native controls. Record portrait atlas cells are left intact.
The copied image uses the host's async file-icon loader, retaining the native
portrait until the texture is uploaded. The original clip and fade alpha remain;
the image is centered proportionally in its rectangle. Native frames, names and
status overlays remain separate, unchanged controls.

Loaded textures no longer shown are released with `UiPainter.Forget`. Baseline
0.22.1 lacks the newer `ForgetIcon` helper, so the corresponding requested-icon
entry is also removed through a validated field. Worker uploads still pending
are tracked until they can be released. All these operations run on the UI thread.
Reflection contracts are validated at initialization against the release binaries.

## Validation and remaining manual checks

`PortraitChecks` exercises actual ModHost registration, both HUD modes, async
chooser completion via temporary executable fixtures, Cancel/fallback, Unicode
paths containing quotes and shell syntax, stale selections, paused apply/reset,
same-template isolation, resize/alpha/orientation, invalid/oversized images,
native save fields and full save folders on disk. It removes the source and
cache before restoring, checks repeated save bytes, native atlas mapping and
non-square fit, reset field removal, corrupt bundles and invalid/dead/reused IDs.
The Windows structure's size is checked against the 32/64-bit ABI.

Manual follow-up: choose a portrait in the desktop's real chooser, check it in
the squad bar, switch squads, open the squads screen and a conversation, save
and reload, and reset. Check transparency and wide/tall pictures at the current
UI scale. Linux headless checks cannot validate GPU presentation or opening a
Windows shell dialog; the Windows STA/PInvoke path needs that native runtime test.
