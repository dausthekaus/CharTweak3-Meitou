using System.Reflection;
using HarmonyLib;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Editing;

namespace CharTweak3;

/// <summary>
/// Opens Meitou's existing in-play editor for exactly one validated player character.
/// </summary>
internal static class CharacterEditorLauncher
{
    /// <summary>
    /// The HUD's editor controller and its native entry point, validated at mod initialization.
    /// </summary>
    private static PropertyInfo? s_editor;
    private static PropertyInfo? s_editorOpen;
    private static MethodInfo? s_openEditor;

    /// <summary>
    /// Checks the installed release's API without patching or replacing the native editor.
    /// </summary>
    internal static void Initialize()
    {
        var ui = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", throwOnError: true)!;
        s_editor = AccessTools.Property(ui, "CharacterEditor")
            ?? throw new MissingMemberException("Unsupported MeitouClient character editor controller.");
        var editorType = s_editor.PropertyType;
        s_editorOpen = AccessTools.Property(editorType, "EditorOpen");
        s_openEditor = AccessTools.DeclaredMethod(editorType, "OpenEditor",
            [typeof(EditKind), typeof(IReadOnlyList<CharacterId>), typeof(CharacterId), typeof(bool)]);
        if (s_editorOpen?.PropertyType != typeof(bool) || s_openEditor?.ReturnType != typeof(bool))
            throw new MissingMethodException("Unsupported MeitouClient character editor entry point.");
    }

    /// <summary>
    /// Runs on the UI thread. The native editor queues its own changes and handles confirmation, camera and pausing.
    /// </summary>
    internal static bool Open(UiFrameContext frame, out string status)
    {
        status = "Select a living player character.";
        if (!PersonalityUiBridge.TryGetEditorTarget(frame, out var playerUi, out var character)) return false;
        var editor = s_editor!.GetValue(playerUi)!;
        if ((bool)s_editorOpen!.GetValue(editor)!)
        {
            status = "Character editor is already open.";
            return false;
        }

        // Recruit is the engine's free in-play edit mode: race and gender are preserved.
        bool opened = (bool)s_openEditor!.Invoke(editor, [EditKind.Recruit, new[] { character }, CharacterId.None, false])!;
        status = opened ? "" : "Character editor is unavailable.";
        return opened;
    }
}
