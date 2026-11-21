using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Editing;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

/// <summary>
/// Checks the editor button against the real native editor without a GPU or game assets.
/// </summary>
internal static class EditorChecks
{
    internal static void Run(ModHost host, PopulationData data, Action<World> useWorld)
    {
        var records = new FcsFile();
        records.Records.Add(new FcsRecord { StringId = "editor-race", Name = "Editor race", RecordType = FcsRecordType.Race });
        data.Db.Apply(records, "editor-check.mod");
        var appearanceRecord = new FcsRecord { StringId = "editor-look", RecordType = FcsRecordType.CharacterAppearance };
        appearanceRecord.References["race"] = [new FcsReference("editor-race", 100, 0, 0)];
        var look = new CharacterAppearance { Race = data.Db.Find("editor-race")! };
        AccessTools.Property(typeof(CharacterAppearance), nameof(CharacterAppearance.Body))
            .SetValue(look, new AppearanceFile { Record = appearanceRecord });

        var system = host.Simulation!.Systems.Single(s => s.Name == "personalities").Create(new SystemBuildContext(data, []))!;
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0,
            systems: [new PlayerSystem(), system]);
        var world = session.World;
        useWorld(world);
        world.Player.Faction = data.PlayerFaction;
        var selected = Spawn(world, look, "Editor Pin", true);
        var other = Spawn(world, look, "Editor Beep", true);
        var npc = Spawn(world, look, "Editor NPC", false);
        world.Player.Selection.AddRange([selected, other]);
        world.TellReady(false);
        world.RunTick();

        var uiType = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        var player = Activator.CreateInstance(uiType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [session, (Func<float, float, float>)((_, _) => 0), (Func<CharacterSnapshot, Vector3>)(c => c.Position)], null)!;
        var screens = new MyGuiScreenHost(new MyGuiResources());
        screens.Register("editor", new MyGuiLayout("", []), modal: true);
        screens.Register("file", new MyGuiLayout("", []), modal: true);
        AccessTools.Field(uiType, "FaithfulScreens").SetValue(player, screens);
        AccessTools.Field(uiType, "FaithfulHud").SetValue(player, new MyGuiLayout("", []));
        var editor = AccessTools.Property(uiType, "CharacterEditor").GetValue(player)!;
        var editorType = editor.GetType();
        AccessTools.Property(editorType, "EditorData").SetValue(editor, (data.Db, Path.GetTempPath()));
        UiBindings Bind() => (UiBindings)AccessTools.Method(uiType, "BindUi").Invoke(player, null)!;

        foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
        {
            var ui = new UiHost();
            var diagnostics = new List<UiDiagnostic>();
            ui.Diagnostic += diagnostics.Add;
            var registrations = host.RegisterUi(extension => ui.Register(extension));
            IReadOnlyList<UiControl> Frame() => ui.Arrange(mode, 800, 600, Bind(),
                new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, 800, 600) });
            UiControl Control(string name) => Frame().Single(c => c.Name == "chartweak3-meitou:" + name);
            void Click(string name) => ui.Dispatch(Control(name));
            void Select(CharacterId id)
            {
                world.Commands.Enqueue(new SelectCommand([id]) { Tick = world.Tick });
                world.ApplyPausedInterfaceCommands();
            }

            Click("open");
            Check(Control("editor").Enabled, "living player with a look can open the editor");
            var staleButton = Control("editor");
            Select(other);
            ui.Dispatch(staleButton);
            Check(!screens.IsOpen("editor"), "stale frame cannot edit a newly selected character");
            Select(selected);
            world.Commands.Enqueue(new SelectCommand([other], Additive: true) { Tick = world.Tick });
            world.ApplyPausedInterfaceCommands();
            Click("editor");
            Check(screens.IsOpen("editor") && screens.HasModal, "native modal editor opens");
            Check(Frame().All(c => c.Name != "chartweak3-meitou:window"), "CharTweak panel closes after editor opens");
            var editing = AccessTools.Field(editorType, "Editor").GetValue(editor)!;
            var editType = editing.GetType();
            var characters = (IReadOnlyList<CharacterId>)AccessTools.Property(editType, "Characters").GetValue(editing)!;
            Check(characters.SequenceEqual([selected]), "only the displayed character is listed, even with multiple selected");
            Check((EditKind)AccessTools.Property(editType, "Kind").GetValue(editing)! == EditKind.Recruit,
                "native free in-play mode preserves race and gender");
            Check(!world.Player.InCreation && screens.GetText("editor", "NameText") == "Editor Pin", "existing character opens without creation mode");
            var drafts = (Dictionary<CharacterId, CharacterDraft>)AccessTools.Property(editType, "Drafts").GetValue(editing)!;
            drafts[selected].Name = "Editor renamed";
            AccessTools.Method(editorType, "ConfirmEditor").Invoke(editor, [false]);
            Check(!screens.IsOpen("editor") && !screens.HasModal, "native CONFIRM closes the editor and releases its modal pause");
            var commands = world.Commands.TakeDue(world.Tick);
            var edit = commands.OfType<EditCharacter>().Single();
            Check(edit.Character == selected && edit.Kind == EditKind.Recruit && edit.Name == "Editor renamed",
                "native CONFIRM queues the edit for the intended character");
            Check(!commands.OfType<FinishCreation>().Any(), "in-play confirmation does not end or restart character creation");

            Click("open");
            world.Commands.Enqueue(new InspectCommand(npc) { Tick = world.Tick });
            world.ApplyPausedInterfaceCommands();
            Check(!Control("editor").Enabled, "inspected NPC cannot open the player editor");
            Click("editor");
            Check(!screens.IsOpen("editor"), "disabled NPC button cannot start an editor session");
            Select(selected);
            AccessTools.Property(editorType, "EditorData").SetValue(editor, null);
            Click("editor");
            Check(!screens.IsOpen("editor") && Control("status").Caption.Contains("unavailable"),
                "failed native open keeps the CharTweak panel available with feedback");
            AccessTools.Property(editorType, "EditorData").SetValue(editor, (data.Db, Path.GetTempPath()));
            Check(diagnostics.Count == 0, "editor actions produce no UI diagnostics");
            foreach (var registration in registrations) registration.Dispose();
            Console.WriteLine($"PASS: {mode} native editor open/confirm, one-character targeting, stale selection/NPC guards and failure feedback.");
        }
        CreationChecks.Run(session, player, editor, screens, selected, other, npc);
    }

    private static CharacterId Spawn(World world, CharacterAppearance look, string name, bool player) =>
        world.Characters.Spawn(new CharacterHot { Health = 100 },
            new CharacterCold { Name = name, RecordId = "hero", IsPlayer = player, Appearance = look }, 0);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
