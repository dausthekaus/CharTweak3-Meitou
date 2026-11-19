# CharTweak 3 for MeitouClient

C# code mod for the CharTweak2 feature port. **0.2.0-beta.2** includes the personality
selector and a button to open the selected player's character editor, available
in both HUD modes after starting or loading a game.
The feature inventory and porting rules are in [docs/migration.md](docs/migration.md).

## Personality selector

1. Select a character and click **CharTweak 3** near the upper-left corner.
2. Check the displayed name and current personality. When inspecting an NPC,
   the selector follows that NPC, like the game's HUD. With multiple characters
   selected, it edits only the one named in the panel.
3. Use **<** and **>** to preview a personality, then press **Apply**.
4. If paused, unpause. Wait for **Personality updated.** before saving normally.

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
**Open Character Editor**. The native editor opens for only the character named
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

## Build on Fedora/Linux

Install a .NET 11 SDK (`dotnet --list-sdks` must show 11.x), Bash, tar and
coreutils. The installed Fedora 43 SDK `11.0.100-rc.3.26522.148` also builds this
scaffold; a stable .NET 11 SDK is preferred for ongoing development. No Wine,
Visual Studio, KenshiLib, RE_Kenshi or separate Harmony package is needed.

Extract the [official standard Linux 0.22.1 release](https://github.com/VlcekM/MeitouClient/releases/tag/v0.22.1).
Use the directory containing `MeitouClient.dll`, `Meitou.Simulation.dll`,
`Meitou.Data.dll`, `Meitou.Core.dll` and `0Harmony.dll`:

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
They use the
release's bundled .NET runtime so Harmony runs even when Fedora's SDK is an RC.
Only temporary test files are written; no Kenshi assets or existing saves are used.

The Release build and checks pass on Fedora 43 against official 0.22.1. The
archive contains only the mod's DLL, data file, manifest, README and license.
The user has confirmed the personality selector works in-game. The editor
button's rendering and interaction still need an in-game check.
