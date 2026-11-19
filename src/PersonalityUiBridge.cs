using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace CharTweak3;

/// <summary>
/// Adds the HUD's exact displayed character and a serially published personality to UI bindings.
/// </summary>
internal static class PersonalityUiBridge
{
    /// <summary>
    /// Binding names are scoped to this mod.
    /// </summary>
    private const string TargetKey = "chartweak3.target", NameKey = "chartweak3.name", EditorKey = "chartweak3.editor";

    /// <summary>
    /// Validated access to the internal HUD selection accessor and its session; no live character state is read here.
    /// </summary>
    private static MethodInfo? s_lead;
    private static FieldInfo? s_session;

    /// <summary>
    /// The active HUD, held weakly so the mod does not retain closed interfaces or worlds.
    /// </summary>
    private static WeakReference<object>? s_playerUi;

    /// <summary>
    /// Installs the binding bridge and refreshes selection views when the host applies paused interface commands.
    /// </summary>
    internal static void Install(Harmony harmony)
    {
        var playerUi = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", throwOnError: true)!;
        var bind = AccessTools.DeclaredMethod(playerUi, "BindUi", Type.EmptyTypes);
        s_lead = AccessTools.DeclaredMethod(playerUi, "Lead", Type.EmptyTypes);
        s_session = AccessTools.DeclaredField(playerUi, "Session");
        if (bind?.ReturnType != typeof(UiBindings) || s_lead?.ReturnType != typeof(CharacterSnapshot)
            || s_session?.FieldType != typeof(WorldSession))
            throw new NotSupportedException("Unsupported MeitouClient HUD selection API.");

        harmony.Patch(bind, postfix: new HarmonyMethod(typeof(PersonalityUiBridge), nameof(Bind)));
        var paused = AccessTools.DeclaredMethod(typeof(World), nameof(World.ApplyPausedInterfaceCommands), Type.EmptyTypes)
            ?? throw new MissingMethodException("Unsupported MeitouClient paused selection API.");
        harmony.Patch(paused, postfix: new HarmonyMethod(typeof(PersonalityUiBridge), nameof(Paused)));
    }

    /// <summary>
    /// Supplies metadata from the same snapshot character used by the game's own HUD.
    /// </summary>
    private static void Bind(object __instance, UiBindings __result)
    {
        if (s_session!.GetValue(__instance) is not WorldSession session) return;
        var system = session.World.Systems.OfType<PersonalitySystem>().FirstOrDefault();
        if (system is null) return;
        if (s_playerUi is null) s_playerUi = new WeakReference<object>(__instance);
        else s_playerUi.SetTarget(__instance);
        if (s_lead!.Invoke(__instance, null) is not CharacterSnapshot lead) return;
        __result.Text[NameKey] = lead.Name;
        if (lead.Body is { Dead: true } || !system.View.TryGetValue(lead.Id, out int tag)) return;
        __result.Text[TargetKey] = FormattableString.Invariant($"{system.Session}|{lead.Id.Slot}|{lead.Id.Generation}|{tag}");
        if (lead.IsPlayer && lead.Appearance is not null) __result.Text[EditorKey] = "true";
    }

    /// <summary>
    /// Whether this frame offers the editor for its living player character.
    /// </summary>
    internal static bool CanOpenEditor(UiFrameContext frame) => frame.Text(EditorKey) == "true";

    /// <summary>
    /// Revalidates the world and current HUD character immediately before opening the native editor.
    /// </summary>
    internal static bool TryGetEditorTarget(UiFrameContext frame, out object? playerUi, out CharacterId character)
    {
        playerUi = null;
        character = CharacterId.None;
        var target = Read(frame);
        if (target is null || !CanOpenEditor(frame) || s_playerUi is null || !s_playerUi.TryGetTarget(out var ui)) return false;
        if (s_session!.GetValue(ui) is not WorldSession session
            || session.World.Systems.OfType<PersonalitySystem>().FirstOrDefault()?.Session != target.Session) return false;
        if (s_lead!.Invoke(ui, null) is not CharacterSnapshot { IsPlayer: true, Appearance: not null } lead
            || lead.Id != target.Character || lead.Body is { Dead: true }) return false;

        playerUi = ui;
        character = lead.Id;
        return true;
    }

    /// <summary>
    /// Paused selection changes are visible immediately; personality edits remain queued for the next simulation tick.
    /// </summary>
    private static void Paused(World __instance) =>
        __instance.Systems.OfType<PersonalitySystem>().FirstOrDefault()?.RefreshView(__instance);

    /// <summary>
    /// Reads an immutable frame target; malformed or missing selection data disables editing.
    /// </summary>
    internal static PersonalityTarget? Read(UiFrameContext frame)
    {
        var parts = frame.Text(TargetKey).Split('|');
        if (parts.Length != 4 || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int slot)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out int generation)
            || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out int tag)) return null;
        return new PersonalityTarget(parts[0], new CharacterId(slot, generation), tag, frame.Text(NameKey));
    }

    /// <summary>
    /// The character's name even when editing is unavailable, for example a dead character.
    /// </summary>
    internal static string Name(UiFrameContext frame) => frame.Text(NameKey, "Select a character");
}

/// <summary>
/// The selected character and personality captured in one UI frame.
/// </summary>
internal sealed record PersonalityTarget(string Session, CharacterId Character, int Tag, string Name)
{
    /// <summary>
    /// Changes when the selected character or running world changes.
    /// </summary>
    internal string Key => $"{Session}:{Character}";
}
