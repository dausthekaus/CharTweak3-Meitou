using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;

namespace CharTweak3;

/// <summary>Copies an appearance before tinting it, so identical races/gear and published snapshots remain isolated.</summary>
internal static class ColourAppearance
{
    private sealed record Source(CharacterAppearance Original, ColourPalette Palette);
    private static readonly ConditionalWeakTable<CharacterAppearance, Source> Sources = new();
    private static readonly MethodInfo Clone = AccessTools.DeclaredMethod(typeof(object), "MemberwiseClone");
    private static FieldInfo s_parts = null!, s_layers = null!;

    internal static void Install(Harmony harmony)
    {
        s_parts = AccessTools.DeclaredField(typeof(CharacterAppearance), "<Parts>k__BackingField");
        s_layers = AccessTools.DeclaredField(typeof(CharacterAppearance), "<BodyLayers>k__BackingField");
        if (s_parts?.FieldType != typeof(List<CharacterPart>) || s_layers?.FieldType != typeof(List<BodyLayer>))
            throw new NotSupportedException("Unsupported MeitouClient appearance collections.");
        var withoutHat = AccessTools.DeclaredMethod(typeof(CharacterAppearance), nameof(CharacterAppearance.WithoutHat),
            [typeof(GameDatabase), typeof(string)]);
        if (withoutHat?.ReturnType != typeof(CharacterAppearance))
            throw new MissingMethodException("Unsupported MeitouClient hidden-hat appearance API.");
        harmony.Patch(withoutHat, postfix: new HarmonyMethod(typeof(ColourAppearance), nameof(WithoutHat)));
    }

    internal static CharacterAppearance Original(CharacterAppearance look) =>
        Sources.TryGetValue(look, out var source) ? source.Original : look;

    internal static bool Dyeable(GameRecord record) => record.Type == FcsRecordType.LimbReplacement
        || record.Type is FcsRecordType.Armour or FcsRecordType.Container && !record.GetBool("dont colorise", true);

    internal static CharacterAppearance Tint(CharacterAppearance look, ColourPalette palette)
    {
        look = Original(look);
        var dye = palette.Dye();
        // Retain all body, pose, attachment and gameplay metadata, detaching the two collections we change.
        // No collection or record belonging to the source appearance is mutated.
        var copy = (CharacterAppearance)Clone.Invoke(look, null)!;
        s_parts.SetValue(copy, look.Parts.Select(p => Dyeable(p.Record) ? p with { Colour = dye } : p).ToList());
        s_layers.SetValue(copy, look.BodyLayers.Select(p => Dyeable(p.Record) ? p with { Colour = dye } : p).ToList());
        Sources.Add(copy, new Source(look, palette));
        return copy;
    }

    private static void WithoutHat(CharacterAppearance __instance, ref CharacterAppearance __result)
    {
        if (!ReferenceEquals(__instance, __result) && Sources.TryGetValue(__instance, out var source))
            __result = Tint(__result, source.Palette);
    }
}
