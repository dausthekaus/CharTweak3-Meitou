using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace CharTweak3;

/// <summary>Replaces only live-character atlas images after arrangement, retaining all native frames and overlays.</summary>
internal static class PortraitRendering
{
    private sealed class Loaded { internal HashSet<string> Paths = new(StringComparer.Ordinal); }
    private static readonly ConditionalWeakTable<object, Loaded> Textures = new();
    private static FieldInfo s_session = null!, s_painter = null!, s_requested = null!;
    private static PropertyInfo s_bar = null!;
    private static MethodInfo s_members = null!, s_rect = null!, s_dialogue = null!;
    private static int s_npcCell, s_playerCell;

    internal static void Install(Harmony harmony)
    {
        var player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        s_session = AccessTools.Field(player, "Session"); s_painter = AccessTools.Field(player, "UiPainter");
        s_requested = AccessTools.Field(typeof(UiPainter), "_requestedIcons");
        s_bar = AccessTools.Property(player, "PortraitBar");
        var bar = s_bar?.PropertyType ?? throw new MissingMemberException("PortraitBar");
        s_members = AccessTools.DeclaredMethod(bar, "PortraitMembers", Type.EmptyTypes);
        s_rect = AccessTools.DeclaredMethod(bar, "PortraitRect", [typeof(int)]);
        s_dialogue = AccessTools.DeclaredMethod(bar, "DialoguePortraitCharacters", Type.EmptyTypes);
        var arrange = AccessTools.DeclaredMethod(player, "ArrangeUi", Type.EmptyTypes);
        if (s_session?.FieldType != typeof(WorldSession) || s_painter?.FieldType != typeof(UiPainter) || s_requested?.FieldType != typeof(HashSet<string>)
            || s_members?.ReturnType != typeof(IReadOnlyList<CharacterSnapshot>) || s_rect?.ReturnType != typeof(string)
            || s_dialogue?.ReturnType != typeof((CharacterId, CharacterId)?) || arrange?.ReturnType != typeof(IReadOnlyList<UiControl>))
            throw new NotSupportedException("Unsupported MeitouClient live portrait API.");
        s_npcCell = (int)(AccessTools.Field(bar, "DialogueNpcCell")?.GetRawConstantValue() ?? throw new MissingFieldException("DialogueNpcCell"));
        s_playerCell = (int)(AccessTools.Field(bar, "DialoguePlayerCell")?.GetRawConstantValue() ?? throw new MissingFieldException("DialoguePlayerCell"));
        harmony.Patch(arrange, postfix: new HarmonyMethod(typeof(PortraitRendering), nameof(Arrange)));
    }

    private static void Arrange(object __instance, ref IReadOnlyList<UiControl> __result)
    {
        if (s_session.GetValue(__instance) is not WorldSession session) return;
        var system = session.World.Systems.OfType<PortraitSystem>().FirstOrDefault();
        var views = system?.View;
        var bar = s_bar.GetValue(__instance)!;
        var members = (IReadOnlyList<CharacterSnapshot>)s_members.Invoke(bar, null)!;
        var images = new Dictionary<string, PortraitView>(StringComparer.Ordinal);
        void Add(CharacterId id, int cell)
        {
            if (views is not null && views.TryGetValue(id, out var view)) images[(string)s_rect.Invoke(bar, [cell])!] = view;
        }
        for (int i = 0; i < members.Count; i++) Add(members[i].Id, i);
        if (s_dialogue.Invoke(bar, null) is ValueTuple<CharacterId, CharacterId> talk)
        { Add(talk.Item1, s_npcCell); Add(talk.Item2, s_playerCell); }
        var painter = (UiPainter?)s_painter.GetValue(__instance);
        var loaded = Textures.GetValue(__instance, _ => new());
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        List<UiControl>? changed = null;
        for (int i = 0; i < __result.Count; i++)
        {
            var control = __result[i];
            if (control.Type != "ImageBox" || control.Properties.GetValueOrDefault("ImageTexture") != "meitou:portraits"
                || !images.TryGetValue(control.Properties.GetValueOrDefault("ImageRect") ?? "", out var view) || !File.Exists(view.Path)) continue;
            wanted.Add(view.Path);
            if (painter is not null && !painter.IconReady(view.Path)) continue; // Keep the native image until the worker/upload finishes.
            changed ??= [.. __result];
            changed[i] = Show(control, view);
        }
        if (painter is not null) foreach (string path in loaded.Paths.Except(wanted))
        {
            string texture = UiPainter.IconTexture(path);
            if (!painter.HasTexture(texture)) { wanted.Add(path); continue; } // A worker upload may still be pending.
            painter.Forget(texture);
            ((HashSet<string>)s_requested.GetValue(painter)!).Remove(texture);
        }
        loaded.Paths = wanted;
        if (changed is not null) __result = changed;
    }

    internal static UiControl Show(UiControl control, PortraitView view)
    {
        var properties = new Dictionary<string, string>(control.Properties);
        properties.Remove("ImageRect"); properties.Remove("ImageFlipV"); properties.Remove("ImageResource");
        properties["ImageTexture"] = UiPainter.IconTexture(view.Path);
        float scale = Math.Min(control.Bounds.Width / view.Width, control.Bounds.Height / view.Height);
        float width = view.Width * scale, height = view.Height * scale;
        return control with
        {
            Properties = properties, Pickable = false, Background = Vector4.Zero,
            Bounds = new(control.Bounds.X + (control.Bounds.Width - width) / 2, control.Bounds.Y + (control.Bounds.Height - height) / 2, width, height),
        };
    }
}
