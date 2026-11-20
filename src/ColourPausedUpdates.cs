using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;

namespace CharTweak3;

/// <summary>Applies visual preferences between ticks without advancing the paused world.</summary>
internal static class ColourPausedUpdates
{
    private static MethodInfo s_publish = null!;
    internal static void Install(Harmony harmony)
    {
        var paused = AccessTools.DeclaredMethod(typeof(World), nameof(World.ApplyPausedInterfaceCommands), Type.EmptyTypes);
        s_publish = AccessTools.DeclaredMethod(typeof(World), "PublishPaused", Type.EmptyTypes);
        if (paused?.ReturnType != typeof(void) || s_publish?.ReturnType != typeof(void))
            throw new NotSupportedException("Unsupported MeitouClient paused color update API.");
        harmony.Patch(paused, postfix: new HarmonyMethod(typeof(ColourPausedUpdates), nameof(Apply)));
    }

    private static void Apply(World __instance)
    {
        var system = __instance.Systems.OfType<ColourSystem>().FirstOrDefault();
        var portraits = __instance.Systems.OfType<PortraitSystem>().FirstOrDefault();
        if (system is null && portraits is null) return;
        // The native method has already checked and completed its tick boundary. Only cold visual preferences change here.
        var due = __instance.Commands.TakeDue(__instance.Tick, c => system is not null && c is SetColours || portraits is not null && c is SetPortrait);
        if (due.Count == 0) return;
        system?.Inputs(__instance, due);
        system?.SlowWorld(__instance);
        portraits?.Inputs(__instance, due);
        portraits?.SlowWorld(__instance);
        s_publish.Invoke(__instance, null);
    }
}
