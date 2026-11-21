# Custom portraits (0.4.0-beta.1)

## Profile extension (0.4.0-beta.2)

The main menu now includes a compact portrait/name/personality/color profile.
`PersonalityUiBridge` binds the current snapshot's appearance and applied color
mode; native equipment uses the first dyeable item, with no fake default when no
dye exists. The profile's swatches are copied controls colored from their bound
RGB arguments; dynamic style override stacks are not accumulated.

The profile uses the exact character's live portrait. While the main menu is
shown and no conversation exists, a scoped postfix on
`PortraitBar.DialoguePortraitCharacters` requests that character in the unused
conversation NPC cell. The host already includes such requests when loading,
posing and rendering portraits, even for an inspected NPC outside the squad bar.
Real conversation requests take priority. Closing the menu or suppressing the HUD
for a title, editor or dialogue removes the extra request. The profile's image
control maps to that cell and then uses the existing custom-image replacement,
preserving aspect ratio. Nothing is stored on CHARACTER templates.

`LauncherVisibility` registers a native boolean Mod setting shared by all HUD
registrations. The main menu's toggle and the shortcut change it without closing
the menu or advancing the world. Its `Changed` event also writes an atomic
`interface.json` preference because baseline 0.22.1 only persists setting changes
made through its own Settings page. This keeps visibility across restarts on all
supported clients. Unreadable preferences are preserved and write failures are
logged without disabling the in-memory toggle.

`ModContext.AddKeyAction` is feature-detected at runtime rather than referenced
from the 0.22.1 compilation baseline. On hosts providing it, an appropriately
typed callback registers `toggle-launcher`, default `Key.Backslash`, in Controls.
On 0.22.1, a `WorldSession.Tick` postfix reads the managed input's press edge, with
current-interface, title, text-focus and existing-game-binding guards. F11 is
unchanged. The newer Controls path needs checking on a host that provides it;
headless checks run against 0.22.1 and cover the compatibility path.

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

## Native rendered portrait customization: feasibility

Reviewed against [0.22.2-nightly.20261121](https://github.com/VlcekM/MeitouClient/releases/tag/v0.22.2-nightly.20261121),
commit `802c1ca3fcec7250d0a22c7be3dd647013cdab2a`, on November 21, 2026.
The broader dynamic styling below remains a proposed extension. The basic still
Photo prototype in 0.5.0-beta.1 is described after these findings.

- **Backgrounds:** feasible through UI composition. The native portrait atlas is
  transparent around the character. `PortraitBar` draws a background, back
  overlay, character image, front overlay, name and frame as separate controls.
  A per-character background can sit behind the native image while keeping
  combat/hunger/hurt indicators and selection behavior. Profile and conversation
  portraits need their own corresponding background controls.
- **Automatic gear updates:** retain the native renderer and its appearance
  asset. Its portrait cache redraws when the character, appearance asset,
  camera serial or radius changes, and when better textures arrive. A custom
  background does not require replacing the character with an imported bitmap.
  Style changes would also need explicit cache invalidation.
- **Camera/framing:** achievable in principle with portrait-only rendering
  hooks. The native camera uses race offsets/distance and a character-specific
  serial for yaw/pitch; these are distinct from a skeletal pose. Per-character
  controls should not modify shared race records.
- **Body poses:** need a rendering prototype. `PosePortrait` currently evaluates
  the idle animation at time zero plus fixed posture layers. `BeginPortraitPose`
  allocates portrait bone/attachment data, and `EndPortraitPose` restores the
  live state. A selectable frozen animation pose must stay inside that scope,
  restore state on every exit, retain the current gear and work with different
  rigs. The public `PortraitRequest` carries only key/cell/serial, and API 3
  exposes no dedicated per-character portrait pose callback. `LookExtras` pose
  weights are shape keys, not a replacement for this skeletal animation work.

A small first implementation could offer native-rendered backgrounds and
camera presets alongside imported images, followed by frozen animation poses
once a GPU prototype confirms the isolation and cache behavior. Use Meitou's
managed UI/rendering path for Linux and Windows, with visual tests on both.

Source: [native portrait rendering](https://github.com/VlcekM/MeitouClient/blob/802c1ca3fcec7250d0a22c7be3dd647013cdab2a/src/Meitou.Game/Rendering/Characters/CharacterRenderer.Portraits.cs),
[portrait UI layers](https://github.com/VlcekM/MeitouClient/blob/802c1ca3fcec7250d0a22c7be3dd647013cdab2a/src/Meitou.Game/Ui/PortraitBar.cs)
and [modding API](https://github.com/VlcekM/MeitouClient/blob/802c1ca3fcec7250d0a22c7be3dd647013cdab2a/docs/modding.md).

## Still Photo prototype (0.5.0-beta.1)

The user chose a still image of the current pose/gear with an optional background.
`PhotoEditor` supplies a draggable native modal, an OS background chooser, turn
and zoom controls, and Picture/Back/Close. It pauses through the existing native
menu-pause mechanism and uses the same target/world revalidation as the editors.

`PhotoRendering` validates and patches the internal entry points: GameHost's
FillDrawList prepares an EditorStageRequest, GatherPortraitKeys keeps that
character posed at any distance, CharacterRenderer.DrawPortraits draws the stage
after the native portraits, and GameLoop.DrawFrame reads it after rendering has
finished. This uses the world's current bones and gear; it does not replace the
live bone palette with the portrait's idle pose. The plain studio background
is temporarily made transparent and restored in a finally block. Other native
stage owners are respected. An unavailable contract disables only Photo.

The stage is 512×512 RGBA. A Picture request is fulfilled on the render thread
with FramebufferCapture.Read on the Vulkan backend. PhotoImage flips the rows
once, rejects an empty capture, and composites the character over a centered
square crop of the chosen background (or neutral gray). PNG creation/storage is
off-thread through PortraitFiles.FromPixels, sharing the existing encoder,
content hashes and limits. A completed result is revalidated before SetPortrait
is queued. The existing system handles paused application, character saves and
the embedded portable image bundle, with no serialization changes.

Closing cancels pending readback and detaches the native stage request. A late
background import/composition cannot assign a portrait after closing. Selection
or world changes reject the result. No live pose, camera, race or equipment
records are modified. Static image import/reset remain available alongside Photo.

PhotoChecks covers the actual hooks' installation, both HUD modes, native modal
dragging/pause, viewport fitting, OS background loading and invalid-image
retention, preview-only framing, compositing/alpha/crop/orientation, existing PNG
storage, synthetic readback completion, stale-selection rejection and pending
capture cancellation. These do not exercise a real GPU. In-game follow-up: test
standing/sitting/action poses, light/dark backgrounds and hair edges, turn/zoom,
Picture, save/reload and Reset on Linux and Windows, on both tested clients.

### Camera controls (0.5.0-beta.2)

The user confirmed the initial photo preview/capture in-game on Linux. Camera
controls now include Head/Full body presets, vertical orbit (bounded to ±1.3
radians), four-way screen-relative panning, fine head zoom and Reset view. Head
starts at distance 9, with 0.5-unit zoom steps down to 2; the full-body preset
keeps the previous distance 42 and 4-unit steps. Switching focus recenters and
retains the chosen yaw. Reset restores all camera defaults without changing
the background. All camera values are temporary UI state.

A scoped postfix on CharacterRenderer.StageCamera handles only this Photo
request. In Head mode it targets BonePosition(key, "Bip01 Head") from the last
live rendered pose, retaining the native head-camera fallback when unavailable.
Eye and pivot receive the same translation, preserving the native yaw/elevation
and distance. Panning follows the camera's right/up axes. Native head mode may
calculate its fallback point from a scratch idle pose, but the renderer still
draws the current live bone palette. Other stage requests are left untouched.

Checks cover head/body presets, propagation to EditorStageRequest, tilt/zoom
limits, screen-relative pan math, live-head centering, native fallback, unrelated
stage isolation, Reset retaining the background, unchanged character state, and
both HUD modes fitting the existing window. New framing still needs a GPU visual
check on Linux and Windows.
