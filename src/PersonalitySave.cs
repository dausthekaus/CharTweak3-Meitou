using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Saving;

namespace CharTweak3;

/// <summary>
/// Persists explicit personalities through the native field that Meitou 0.22.1 already reads.
/// </summary>
internal static class PersonalitySave
{
    /// <summary>
    /// Weak character keys survive squad moves without retaining characters or worlds after they are removed.
    /// </summary>
    private static readonly ConditionalWeakTable<CharacterCold, object> Marked = new();

    /// <summary>
    /// Installs narrowly scoped read/write hooks, without changing naturally rolled personalities in saves.
    /// </summary>
    internal static void Install(Harmony harmony)
    {
        var parameters = new[] { typeof(FcsRecord), typeof(CharacterCold) };
        var read = AccessTools.DeclaredMethod(typeof(DialogueSave), nameof(DialogueSave.Read), parameters)
            ?? throw new MissingMethodException("Unsupported MeitouClient personality save reader.");
        var write = AccessTools.DeclaredMethod(typeof(DialogueSave), nameof(DialogueSave.Write), parameters)
            ?? throw new MissingMethodException("Unsupported MeitouClient personality save writer.");
        harmony.Patch(read, postfix: new HarmonyMethod(typeof(PersonalitySave), nameof(Read)));
        harmony.Patch(write, postfix: new HarmonyMethod(typeof(PersonalitySave), nameof(Write)));
    }

    /// <summary>
    /// Marks a character whose personality must be written and hashed.
    /// </summary>
    internal static void Mark(CharacterCold cold) => Marked.GetValue(cold, _ => new object());

    /// <summary>
    /// Whether the character has an explicit personality from a save or this mod's edit.
    /// </summary>
    internal static bool IsMarked(CharacterCold cold) => Marked.TryGetValue(cold, out _);

    /// <summary>
    /// Keeps a loaded explicit value explicit on subsequent saves, including when its squad changes.
    /// </summary>
    private static void Read(FcsRecord? state, CharacterCold cold)
    {
        if (state?.Ints.GetValueOrDefault("personality") is > Personality.None and < Personality.Count) Mark(cold);
    }

    /// <summary>
    /// Adds only a marked personality; ordinary dialogue locks remain entirely the engine's responsibility.
    /// </summary>
    private static void Write(FcsRecord state, CharacterCold cold)
    {
        if (IsMarked(cold) && cold.Personality is > Personality.None and < Personality.Count)
            state.Ints["personality"] = cold.Personality;
    }
}
