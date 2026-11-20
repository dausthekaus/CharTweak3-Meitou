# CharTweak2 feature reference

Source inspected: the mounted Windows `MYmods/CharTweak2/` project, principally
`CharTweak.cpp` (startup identifies itself as **1.3.0 shared-player-uniform**),
`PortraitOverride.cpp/.h` and `MaterialIsolation.cpp/.h`. `BUILD-ME.txt` describes
an older 1.1.0 build; `CharTweak2 - copia.cpp` is a backup, not the primary reference.
The legacy source and its bundled dependencies remain outside this repository.

The [personality selector](../README.md#personality-selector) and
[player character editor button](../README.md#character-editor) are implemented
in 0.4.0-beta.2, along with independent personality drafts in the native new-game
editor, personal clothing colors, shared uniform membership and saved palettes.
See [the color implementation](character-colours.md) and
[custom portraits](custom-portraits.md). The main menu now includes a selected-character
profile. Launcher visibility has a backslash shortcut and a native Mods setting;
native Controls rebinding is used on clients exposing `AddKeyAction`, with a
managed compatibility shortcut on 0.22.1. The old global F11 polling is not used.

| Feature observed in CharTweak2 | Reference implementation |
| --- | --- |
| Selected-character customization window | `createMenu`, `refreshSelectedCharacter`, `toggleMenu` |
| Cycle 10 personalities, including Normal | `PERSONALITIES`, `cyclePersonality` |
| Open the appearance editor for player characters only | `openCharacterEditor`; NPC guard prevents a softlock |
| Assign/remove custom PNG portraits and open the portrait folder | `onSetPortrait`, `onRemovePortrait`, `PortraitOverride::chooseAndAssign` |
| Keep portrait assignments through saves and refresh recreated widgets | `PortraitOverride::loadAssignments`, `saveAssignments`, `monitorCachedWidgets` |
| Shared player uniform with primary/secondary RGB, presets and hex input | `PlayerUniformData`, `applyPlayerUniformColors`, `applyHexColor` |
| Join/leave the uniform per character; update all members | `usePlayerUniform`, `leavePlayerUniform`, `refreshUniformCharacter` |
| Persist shared colors and membership across saves | save/load hooks and membership sidecars |
| Named palettes: save, overwrite, select, delete | `readSavedPalettes`, `writeSavedPalettes`, `onSavePalette`, `onDeletePalette` |
| Rebind the menu key and remember it | `processMenuKeyRebind`, `settings.ini`; old default F11 |
| Isolate appearance materials to avoid tinting unrelated characters | `MaterialIsolation::installHooks` |

## Porting rules

- Register feature systems through `ModContext.AddSystem` factories: a new world
  is built for each new game or load. Keep per-world state in those systems.
- UI uses `AddUi(IUiExtension)` and Meitou UI types. Use `AddKeyAction` where the
  client exposes it, with rebinding on Controls; 0.22.1 needs a managed input
  compatibility path. Choose an available default:
  Meitou already uses F11. Win32 polling and MyGUI widgets do not transfer.
- UI and key callbacks read snapshots and enqueue `SimCommand` values. Apply
  gameplay changes in a serial simulation phase; hash state that affects simulation.
- For future features, use `IModSaveData` and lasting ids on clients that provide
  that API. The 0.22.1 baseline has no lasting ids; the current personality beta
  persists through native character fields, with `IModSaveData` for the active
  uniform and embedded portrait images. Never persist transient
  `CharacterId` values, pointers or old KenshiLib handles as character identities.
- Use `AddSettings` for global preferences that fit its supported property types.
  Named palettes and imported portraits need a deliberate storage design.
- Use the game's Harmony instance (`context.Harmony`) only when a feature needs
  a patch. Inspect the release's actual APIs before choosing patch targets.
- OGRE material hooks, native detours, Win32 file dialogs, old sidecar parsing
  and appearance-editor calls require new implementations against Meitou.
  Legacy sidecars are not imported automatically.

See the [official contract](https://github.com/VlcekM/MeitouClient/blob/4edc8a4c67712e64151e5de6175e404dfc9791c7/docs/modding.md)
for lifecycle, threading, persistence, UI and input. Verify APIs against the
selected release's binaries as well as newer source docs. Check each feature's
save/load and UI behavior before enabling it.
