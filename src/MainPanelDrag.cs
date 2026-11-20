using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>A transparent native title strip gives the HUD extension the host's normal window dragging.</summary>
internal static class MainPanelDrag
{
    internal const string Screen = "chartweak3-menu-drag", Root = "chartweak3-menu-title";
    private sealed class Placement { internal Vector2 Centre; internal float Width; }
    private static readonly ConditionalWeakTable<MyGuiScreenHost, Placement> Placements = new();
    private static FieldInfo s_screens = null!;

    internal static void Initialize(Harmony harmony)
    {
        var player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        s_screens = AccessTools.DeclaredField(player, "FaithfulScreens");
        if (s_screens?.FieldType != typeof(MyGuiScreenHost)) throw new NotSupportedException("Unsupported MeitouClient menu drag host.");
        var arrange = AccessTools.DeclaredMethod(player, "ArrangeBase", [typeof(bool)]);
        if (arrange?.ReturnType != typeof(List<UiControl>)) throw new NotSupportedException("Unsupported MeitouClient HUD arrangement API.");
        harmony.Patch(arrange, prefix: new HarmonyMethod(typeof(MainPanelDrag), nameof(HideWithoutHud)));
    }

    private static void HideWithoutHud(object __instance, bool hud)
    {
        if (!hud && s_screens.GetValue(__instance) is MyGuiScreenHost screens && screens.IsOpen(Screen)) screens.Close(Screen);
    }

    private static MyGuiScreenHost? Screens(UiFrameContext frame) => PersonalityUiBridge.InterfaceFor(frame) is { } player
        ? (MyGuiScreenHost?)s_screens.GetValue(player) : null;

    internal static UiRect Place(UiFrameContext frame, float width, float height)
    {
        var centre = new Vector2((frame.Viewport.Width - width) / 2, (frame.Viewport.Height - height) / 2);
        if (Screens(frame) is not { } screens) return new(centre.X, centre.Y, width, height);
        if (!screens.IsRegistered(Screen))
        {
            var title = new MyGuiWidget("Window", Root, "", "", "", new(0, 0, width, 32), false,
                new Dictionary<string, string> { ["Movable"] = "true" }, new Dictionary<string, string>(), []);
            screens.Register(Screen, new MyGuiLayout("", [title]));
        }
        var placement = Placements.GetValue(screens, _ => new Placement());
        placement.Centre = centre; placement.Width = width;
        var offset = screens.GetOffset(Screen);
        var position = new Vector2(Math.Clamp(centre.X + offset.X, 0, Math.Max(0, frame.Viewport.Width - width)),
            Math.Clamp(centre.Y + offset.Y, 0, Math.Max(0, frame.Viewport.Height - height)));
        if (position != centre + offset) screens.SetOffset(Screen, position - centre);
        if (!screens.IsOpen(Screen)) screens.Open(Screen);
        return new(position.X, position.Y, width, height);
    }

    internal static void Hide(UiFrameContext frame)
    {
        if (Screens(frame) is { } screens && screens.IsOpen(Screen)) screens.Close(Screen);
    }

    internal static UiBindings Bind(MyGuiScreenHost screens)
    {
        var b = new UiBindings();
        if (!Placements.TryGetValue(screens, out var placement)) return b;
        b.WindowPosition = placement.Centre;
        b.Positions[Root] = new(0, 0, placement.Width, 32);
        return b;
    }
}
