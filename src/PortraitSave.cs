using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace CharTweak3;

/// <summary>Image identity follows the native character record, never a name, slot, or shared template.</summary>
internal static class PortraitSave
{
    private sealed record Assignment(string Id);
    private const string Key = "chartweak3.portrait.v1";
    private static readonly ConditionalWeakTable<CharacterCold, Assignment> Assignments = new();
    internal static string? Get(CharacterCold cold) => Assignments.TryGetValue(cold, out var value) ? value.Id : null;
    internal static void Set(CharacterCold cold, string? id)
    {
        Assignments.Remove(cold);
        if (PortraitFiles.ValidId(id)) Assignments.Add(cold, new(id!));
    }
    internal static void Install(Harmony harmony)
    {
        foreach (string name in new[] { nameof(DialogueSave.Read), nameof(DialogueSave.Write) })
        {
            var method = AccessTools.DeclaredMethod(typeof(DialogueSave), name, [typeof(FcsRecord), typeof(CharacterCold)])
                ?? throw new MissingMethodException("Unsupported MeitouClient portrait save API.");
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PortraitSave), name));
        }
    }
    private static void Read(FcsRecord? state, CharacterCold cold) => Set(cold, state?.Strings.GetValueOrDefault(Key));
    private static void Write(FcsRecord state, CharacterCold cold)
    {
        state.Strings.Remove(Key);
        if (Get(cold) is { } id) state.Strings[Key] = id;
    }
}
