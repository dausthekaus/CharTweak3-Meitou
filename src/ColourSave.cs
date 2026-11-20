using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace CharTweak3;

/// <summary>Per-character preferences follow native character save records, including squad moves and imports.</summary>
internal static class ColourSave
{
    private const string Version = "chartweak3.colour.version", Mode = "chartweak3.colour.mode",
        Primary = "chartweak3.colour.primary", Secondary = "chartweak3.colour.secondary";
    private static readonly ConditionalWeakTable<CharacterCold, ColourAssignment> Assignments = new();

    internal static void Install(Harmony harmony)
    {
        foreach (var name in new[] { nameof(DialogueSave.Read), nameof(DialogueSave.Write) })
        {
            var method = AccessTools.DeclaredMethod(typeof(DialogueSave), name, [typeof(FcsRecord), typeof(CharacterCold)])
                ?? throw new MissingMethodException("Unsupported MeitouClient character color save API.");
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(ColourSave), name));
        }
    }

    internal static ColourAssignment? Get(CharacterCold cold) => Assignments.TryGetValue(cold, out var value) ? value : null;
    internal static void Set(CharacterCold cold, ColourAssignment? value)
    {
        Assignments.Remove(cold);
        if (value is not null) Assignments.Add(cold, value);
    }

    private static void Read(FcsRecord? state, CharacterCold cold)
    {
        Set(cold, null);
        if (state?.Ints.GetValueOrDefault(Version) != 1 || !state.Ints.TryGetValue(Primary, out int primary)
            || !state.Ints.TryGetValue(Secondary, out int secondary)) return;
        var mode = (ColourMode)state.Ints.GetValueOrDefault(Mode);
        var palette = new ColourPalette(primary, secondary);
        if (mode is ColourMode.Personal or ColourMode.Uniform && palette.Valid) Set(cold, new(mode, palette));
    }

    private static void Write(FcsRecord state, CharacterCold cold)
    {
        // Remove old fields too: SaveCapture can reuse a record after the player presses Reset.
        state.Ints.Remove(Version);
        state.Ints.Remove(Mode);
        state.Ints.Remove(Primary);
        state.Ints.Remove(Secondary);
        if (Get(cold) is not { } assignment) return;
        state.Ints[Version] = 1;
        state.Ints[Mode] = (int)assignment.Mode;
        state.Ints[Primary] = assignment.Palette.Primary;
        state.Ints[Secondary] = assignment.Palette.Secondary;
    }
}
