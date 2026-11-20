using System.Numerics;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Editing;
using Meitou.Simulation.Saving;

/// <summary>
/// Exercises the real editor decorator, modal pointer routing and confirmation lifecycle with a starting squad.
/// </summary>
internal static class CreationChecks
{
    internal static void Run(WorldSession session, object player, object editor, MyGuiScreenHost screens,
        CharacterId first, CharacterId second, CharacterId npc)
    {
        var world = session.World;
        var uiType = player.GetType();
        var editorType = editor.GetType();
        var open = AccessTools.Method(editorType, "OpenEditor");
        var confirm = AccessTools.Method(editorType, "ConfirmEditor");
        var close = AccessTools.Method(editorType, "CloseEditor");
        var action = AccessTools.Method(editorType, "EditorAction");
        screens.Activated += e => action.Invoke(editor, [e]);
        AccessTools.Field(uiType, "Width").SetValue(player, 1280);
        AccessTools.Field(uiType, "Height").SetValue(player, 720);
        UiBindings Bind(string _) => new();
        void Decorate(string _, List<UiControl> controls) => AccessTools.Method(editorType, "AddEditorContent").Invoke(editor, [controls]);
        IReadOnlyList<UiControl> Frame() => screens.Arrange(1280, 720, Bind, Decorate).Single(s => s.Id == "editor").Controls;
        UiControl Control(string name) => Frame().Single(c => c.Name == "chartweak3.creation." + name);
        void Click(string name)
        {
            var c = Control(name);
            Check(screens.PointerDown(new Vector2(c.Bounds.X + c.Bounds.Width / 2, c.Bounds.Y + c.Bounds.Height / 2)), "modal routes picker click");
        }
        void Switch() => action.Invoke(editor, [new MyGuiEvent("editor", "CharRightArrow", "CharRightArrow")]);
        void Open(EditKind kind, params CharacterId[] characters)
        {
            world.Player.InCreation = kind == EditKind.Creation;
            Check((bool)open.Invoke(editor, [kind, characters, CharacterId.None, false])!, "native editor opens");
            // Apply the native paused selection command; personality drafts must never be commands yet.
            world.ApplyPausedInterfaceCommands();
        }
        void DrainNativeCommands() => world.Commands.TakeDue(world.Tick);

        foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
        {
            AccessTools.Property(uiType, "UiMode").SetValue(player, mode);
            world.Characters.Cold(first.Slot)!.Personality = 0;
            world.Characters.Cold(second.Slot)!.Personality = 0;
            Open(EditKind.Creation, first, second);
            Check(Control("choice").Caption == "Default (automatic)", "new creation starts with engine default");
            Check(screens.FocusNext(false) && screens.ActivateFocused(), "native keyboard navigation reaches personality arrows");
            Check(Control("choice").Caption == "Maniacal", "keyboard activation changes draft");
            Click("next");
            var stale = Control("next");
            Click("previous");
            Check(Control("choice").Caption == "Maniacal", "previous wraps default to final tag");
            Switch();
            Check(Control("choice").Caption == "Default (automatic)", "second character has an independent draft");
            action.Invoke(editor, [new MyGuiEvent("editor", stale.Name, stale.Action)]);
            Check(Control("choice").Caption == "Default (automatic)", "stale first-character frame cannot edit second");
            Click("next");
            Check(Control("choice").Caption == "Honorable", "second character receives its own choice");
            Switch();
            Check(Control("choice").Caption == "Maniacal", "switching back restores the first draft");
            Check(world.Characters.Cold(first.Slot)!.Personality == 0 && world.Characters.Cold(second.Slot)!.Personality == 0,
                "browsing drafts leaves live personalities unchanged");
            var pending = world.Commands.TakeDue(world.Tick);
            Check(pending.All(c => c.GetType().Name != "SetPersonality"), "no personality commands before confirmation");
            foreach (var c in pending) world.Commands.Enqueue(c);
            confirm.Invoke(editor, [false]);
            Check(!screens.IsOpen("editor"), "visited squad confirms normally");
            world.RunTick();
            Check(world.Characters.Cold(first.Slot)!.Personality == 14 && world.Characters.Cold(second.Slot)!.Personality == 1,
                "native confirmation applies both choices on first gameplay tick");
            Check(!world.Player.InCreation, "native confirmation finishes creation");
            var state = new FcsRecord();
            DialogueSave.Write(state, world.Characters.Cold(first.Slot)!);
            var loaded = new CharacterCold();
            DialogueSave.Read(state, loaded);
            Check(loaded.Personality == 14, "creation choice uses native persistent personality field");

            // Starting over/cancelling must discard the entire UI-only draft, even for identical character IDs.
            Open(EditKind.Creation, first, second);
            Check(Control("choice").Caption == "Default (automatic)", "new opening does not inherit old choices");
            action.Invoke(editor, [new MyGuiEvent("editor", stale.Name, stale.Action)]);
            Check(Control("choice").Caption == "Default (automatic)", "stale frame from previous opening cannot change new draft");
            Click("next");
            close.Invoke(editor, null);
            Check(world.Commands.TakeDue(world.Tick).All(c => c.GetType().Name != "SetPersonality"), "closing without confirmation queues no edit");
            Open(EditKind.Creation, first, second);
            Check(Control("choice").Caption == "Default (automatic)", "cancelled draft stays discarded");
            // Complete a full cycle back to default; this must preserve an existing engine value, not force a tag.
            for (int i = 0; i < 11; i++) Click("next");
            Check(Control("choice").Caption == "Default (automatic)", "all choices cycle back to default");
            DrainNativeCommands();
            confirm.Invoke(editor, [true]);
            var defaults = world.Commands.TakeDue(world.Tick);
            Check(defaults.OfType<FinishCreation>().Count() == 1 && defaults.All(c => c.GetType().Name != "SetPersonality"),
                "default-only confirmation sends no personality overrides");

            // A warning for an unvisited squad member must not prematurely commit; accepting it commits once.
            Open(EditKind.Creation, first, second);
            Click("next");
            screens.RegisterIfMissing("message");
            confirm.Invoke(editor, [false]);
            Check(screens.IsOpen("editor"), "native unvisited-character warning keeps editor open");
            Check(world.Commands.TakeDue(world.Tick).All(c => c.GetType().Name != "SetPersonality"), "warning alone does not commit choices");
            // ConfirmEditor(true) is precisely the native warning's acceptance callback.
            confirm.Invoke(editor, [true]);
            Check(world.Commands.TakeDue(world.Tick).Count(c => c.GetType().Name == "SetPersonality") == 1,
                "accepted warning commits exactly once");
            screens.Close("message");

            Open(EditKind.Recruit, first);
            Check(Frame().All(c => !c.Name.StartsWith("chartweak3.creation.", StringComparison.Ordinal)), "in-play editor is unchanged");
            close.Invoke(editor, null);
            DrainNativeCommands();
            Open(EditKind.Creation, npc);
            Check(!Control("next").Enabled, "non-player creation targets cannot receive overrides");
            close.Invoke(editor, null);
            DrainNativeCommands();
            Console.WriteLine($"PASS: {mode} creation picker, modal clicks, independent drafts, stale frames, default/cancel, native warning/confirm and persistence.");
        }
        world.Player.InCreation = false;
    }

    private static void RegisterIfMissing(this MyGuiScreenHost screens, string name)
    {
        if (!screens.IsRegistered(name)) screens.Register(name, new MyGuiLayout("", []), modal: true);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
