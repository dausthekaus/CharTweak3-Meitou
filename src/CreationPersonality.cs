using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Editing;

namespace CharTweak3;

/// <summary>
/// Adds per-character personality drafts to the native new-game editor. CONFIRM commits them with the native edits.
/// </summary>
internal static class CreationPersonality
{
    private const string Prefix = "chartweak3.creation.";
    private static readonly ConditionalWeakTable<object, Drafts> s_drafts = new();
    private static FieldInfo s_editor = null!, s_player = null!, s_session = null!, s_width = null!, s_height = null!;
    private static PropertyInfo s_kind = null!, s_current = null!, s_animals = null!;

    /// <summary>
    /// UI-only choices belong to one editor opening and one world; zero leaves the engine's personality unchanged.
    /// </summary>
    private sealed class Drafts(WorldSession session, string token)
    {
        internal WorldSession Session { get; } = session;
        internal string Token { get; } = token;
        internal string Opening { get; } = Guid.NewGuid().ToString("N");
        internal Dictionary<CharacterId, int> Choices { get; } = [];
    }

    /// <summary>
    /// Validates the release's editor hooks before registering them. Both frontends share this native screen.
    /// </summary>
    internal static void Install(Harmony harmony)
    {
        var player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        var editor = AccessTools.Property(player, "CharacterEditor")!.PropertyType;
        s_editor = AccessTools.DeclaredField(editor, "Editor");
        s_player = editor.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == player);
        s_session = AccessTools.DeclaredField(player, "Session");
        s_width = AccessTools.DeclaredField(player, "Width");
        s_height = AccessTools.DeclaredField(player, "Height");
        var opening = s_editor?.FieldType ?? throw new MissingFieldException("Unsupported MeitouClient editor session.");
        s_kind = AccessTools.Property(opening, "Kind");
        s_current = AccessTools.Property(opening, "Current");
        s_animals = AccessTools.Property(opening, "Animals");
        if (s_kind?.PropertyType != typeof(EditKind) || s_current?.PropertyType != typeof(CharacterId)
            || s_animals?.PropertyType != typeof(IReadOnlySet<CharacterId>) || s_session?.FieldType != typeof(WorldSession)
            || s_width?.FieldType != typeof(int) || s_height?.FieldType != typeof(int))
            throw new NotSupportedException("Unsupported MeitouClient creation editor API.");

        MethodInfo Method(string name, params Type[] arguments) => AccessTools.DeclaredMethod(editor, name, arguments)
            ?? throw new MissingMethodException($"Unsupported MeitouClient editor hook: {name}.");
        harmony.Patch(Method("AddEditorContent", typeof(List<UiControl>)),
            postfix: new HarmonyMethod(typeof(CreationPersonality), nameof(Arrange)));
        harmony.Patch(Method("EditorAction", typeof(MyGuiEvent)),
            prefix: new HarmonyMethod(typeof(CreationPersonality), nameof(Action)));
        harmony.Patch(Method("ConfirmEditor", typeof(bool)),
            prefix: new HarmonyMethod(typeof(CreationPersonality), nameof(BeforeConfirm)),
            postfix: new HarmonyMethod(typeof(CreationPersonality), nameof(AfterConfirm)));
    }

    private static bool TryDrafts(object editor, out object opening, out Drafts drafts)
    {
        opening = s_editor.GetValue(editor)!;
        drafts = null!;
        if (opening is null || (EditKind)s_kind.GetValue(opening)! != EditKind.Creation) return false;
        var session = (WorldSession)s_session.GetValue(s_player.GetValue(editor))!;
        var system = session.World.Systems.OfType<PersonalitySystem>().FirstOrDefault();
        if (system is null) return false;
        drafts = s_drafts.GetValue(opening, _ => new Drafts(session, system.Session));
        return drafts.Session == session && drafts.Token == system.Session;
    }

    private static bool CanChoose(object opening, Drafts drafts, CharacterId id) =>
        !((IReadOnlySet<CharacterId>)s_animals.GetValue(opening)!).Contains(id)
        && drafts.Session.CurrentSnapshot.Characters.Any(c => c.Id == id && c.IsPlayer && c.Body is not { Dead: true });

    private static string Address(Drafts drafts, CharacterId id, int direction) =>
        FormattableString.Invariant($"{Prefix}{drafts.Token}|{drafts.Opening}|{id.Slot}|{id.Generation}|{direction}");

    /// <summary>
    /// Adds controls to the editor's own modal hit-testing and keyboard navigation, between its side panels.
    /// </summary>
    private static void Arrange(object __instance, List<UiControl> controls)
    {
        if (!TryDrafts(__instance, out var opening, out var drafts)) return;
        var id = (CharacterId)s_current.GetValue(opening)!;
        bool enabled = CanChoose(opening, drafts, id);
        var player = s_player.GetValue(__instance);
        float viewportWidth = (int)s_width.GetValue(player)!, viewportHeight = (int)s_height.GetValue(player)!;
        var namePanel = controls.FirstOrDefault(c => c.Name == "NamePanel");
        float left = namePanel is null ? viewportWidth * 0.161458f : namePanel.Bounds.X + namePanel.Bounds.Width;
        float right = controls.FirstOrDefault(c => c.Name == "TabsPanel")?.Bounds.X ?? viewportWidth * 0.7f;
        float width = Math.Min(360, Math.Max(0, right - left - 16));
        if (width < 160 || viewportHeight < 120) return;
        var box = new UiRect(left + (right - left - width) / 2, viewportHeight - 104, width, 88);
        var text = new Dictionary<string, string> { ["TextAlign"] = "Center VCenter" };
        UiControl Control(string name, string type, UiRect bounds, string caption, string action = "") =>
            new(Prefix + name, type, bounds, caption, action, "", enabled, text,
                type == "Button" ? new Vector4(0.24f, 0.22f, 0.18f, 1) : new Vector4(0.10f, 0.10f, 0.09f, 1),
                TextColour: new Vector4(0.9f, 0.9f, 0.85f, 1),
                HoverColour: new Vector4(0.36f, 0.33f, 0.27f, 1), Pickable: type == "Button");
        controls.Add(Control("panel", "Widget", box, "") with { Pickable = true });
        controls.Add(Control("title", "TextBox", new(box.X, box.Y + 4, width, 22), "Personality"));
        int tag = drafts.Choices.GetValueOrDefault(id);
        string label = !enabled ? "Unavailable for this character" : tag == 0 ? "Default (automatic)" : PersonalityOptions.Label(tag);
        controls.Add(Control("choice", "TextBox", new(box.X + 40, box.Y + 30, width - 80, 26), label));
        controls.Add(Control("previous", "Button", new(box.X + 8, box.Y + 28, 28, 30), "<", Address(drafts, id, -1)));
        controls.Add(Control("next", "Button", new(box.X + box.Width - 36, box.Y + 28, 28, 30), ">", Address(drafts, id, 1)));
        controls.Add(Control("hint", "TextBox", new(box.X, box.Y + 60, width, 22), "Saved when you press Confirm"));
    }

    /// <summary>
    /// Only the current character's matching frame can change a draft. Vanilla editor actions keep their own handling.
    /// </summary>
    private static bool Action(object __instance, MyGuiEvent e)
    {
        if (!e.Widget.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        if (e.Screen != "editor" || !TryDrafts(__instance, out var opening, out var drafts)) return false;
        var id = (CharacterId)s_current.GetValue(opening)!;
        if (!CanChoose(opening, drafts, id)) return false;
        int direction = e.Widget == Prefix + "previous" ? -1 : e.Widget == Prefix + "next" ? 1 : 0;
        if (direction == 0 || e.Action != Address(drafts, id, direction)) return false;
        int tag = drafts.Choices.GetValueOrDefault(id);
        int count = PersonalityOptions.All.Length + 1;
        int index = (tag == 0 ? 0 : PersonalityOptions.IndexOf(tag) + 1);
        index = (index + direction + count) % count;
        if (index == 0) drafts.Choices.Remove(id);
        else drafts.Choices[id] = PersonalityOptions.All[index - 1].Tag;
        return false;
    }

    private static void BeforeConfirm(object __instance, out object? __state) =>
        __state = TryDrafts(__instance, out var opening, out _) ? opening : null;

    /// <summary>
    /// The native warning for unvisited characters must actually be accepted before any personality command is sent.
    /// </summary>
    private static void AfterConfirm(object __instance, object? __state)
    {
        if (__state is null || s_editor.GetValue(__instance) is not null || !s_drafts.TryGetValue(__state, out var drafts)) return;
        s_drafts.Remove(__state);
        var world = drafts.Session.World;
        if (world.Systems.OfType<PersonalitySystem>().FirstOrDefault()?.Session != drafts.Token) return;
        foreach (var (id, tag) in drafts.Choices.OrderBy(p => p.Key.Slot).ThenBy(p => p.Key.Generation))
            if (CanChoose(__state, drafts, id))
                world.Commands.Enqueue(new SetPersonality(drafts.Token, id, tag) { Tick = world.Tick });
    }
}
