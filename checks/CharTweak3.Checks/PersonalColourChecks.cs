using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Editing;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Items;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;

/// <summary>Real menu, native text/slider input, simulation, character saves and mod sidecar integration.</summary>
internal static class PersonalColourChecks
{
    private const string Screen = "chartweak3-colours";
    internal static void Run(ModHost host, GameDatabase db, Action<World> useWorld)
    {
        var records = new FcsFile();
        var race = new FcsRecord { StringId = "personal-colour-race", RecordType = FcsRecordType.Race };
        var hero = new FcsRecord { StringId = "personal-colour-hero", RecordType = FcsRecordType.Character };
        hero.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
        var dye = new FcsRecord { StringId = "personal-colour-original", RecordType = FcsRecordType.ColorData };
        dye.Ints["color 1"] = 0xCCAA88; dye.Ints["color 2"] = 0x334455;
        FcsRecord Armour(string id, int slot, bool protectedColour)
        {
            var armour = new FcsRecord { StringId = id, RecordType = FcsRecordType.Armour };
            armour.Ints["slot"] = slot; armour.Bools["dont colorise"] = protectedColour;
            armour.Filenames["mesh"] = "probe.mesh";
            if (slot == (int)AttachSlot.Shirt)
            { armour.Filenames["vest texture"] = "probe.dds"; armour.Filenames["vest colormap"] = "probe-map.dds"; }
            armour.References["color"] = [new FcsReference(dye.StringId, 100, 0, 0)];
            return armour;
        }
        records.Records.AddRange([race, hero, dye, Armour("personal-shirt", (int)AttachSlot.Shirt, false),
            Armour("personal-hat", (int)AttachSlot.Hat, false), Armour("personal-protected", (int)AttachSlot.Body, true)]);
        db.Apply(records, "personal-colour-check.mod");
        var data = PopulationData.Create(db, [], new GeneratedAppearances(db, Path.GetTempPath()));
        var body = new FcsRecord { StringId = "personal-colour-body", RecordType = FcsRecordType.CharacterAppearance };
        body.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
        body.Floats["Height"] = 100;
        CharacterAppearance Build(Inventory inventory) => CharacterAppearance.Build(db, Path.GetTempPath(), hero.StringId,
            new CharacterOptions { Loadout = SavedKit.LoadoutOf(body, SavedKit.FromInventory(inventory), db, 1) });
        Inventory Kit()
        {
            var inventory = new Inventory();
            foreach (var (id, section) in new[] { ("personal-shirt", "shirt"), ("personal-hat", "head"), ("personal-protected", "armour") })
                inventory.Items.Add(new ItemInstance { Record = id, Type = FcsRecordType.Armour, Section = section, ColourId = dye.StringId });
            return inventory;
        }
        ITickSystem[] Systems() => [new PlayerSystem(data), .. host.Simulation!.Systems.Select(f => f.Create(new SystemBuildContext(data, []))!)];
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: Systems());
        var world = session.World;
        useWorld(world);
        world.Player.Faction = data.PlayerFaction;
        var baseKit = Kit();
        var original = Build(baseKit);
        CharacterId Spawn(string name, bool isPlayer = true)
        {
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "check", Faction = data.PlayerFaction };
            world.Squads.Add(squad);
            var id = world.Characters.Spawn(new CharacterHot { Health = 100 }, new CharacterCold
            { Name = name, RecordId = hero.StringId, IsPlayer = isPlayer, Faction = squad.Faction, SquadId = squad.Id, Inventory = Kit(), Appearance = original }, 0);
            squad.Members.Add(id); squad.Leader = id;
            return id;
        }
        var first = Spawn("Color Pin"); var second = Spawn("Color Beep"); var untouched = Spawn("Color Unchanged"); var npc = Spawn("Color NPC", false);
        world.Player.Selection.Add(first); world.TellReady(false); world.RunTick();
        var colours = world.Systems.Single(s => s.GetType().Name == "ColourSystem");
        var mod = host.Mods.Single().Assemblies.Single();
        var command = mod.GetType("CharTweak3.SetColours", true)!;
        var palette = mod.GetType("CharTweak3.ColourPalette", true)!;
        var modeType = mod.GetType("CharTweak3.ColourMode", true)!;
        string Token(ITickSystem system) => (string)AccessTools.Property(system.GetType(), "Session").GetValue(system)!;
        SimCommand Edit(CharacterId id, int mode, int primary, int secondary, string? token = null) =>
            (SimCommand)Activator.CreateInstance(command, token ?? Token(colours), id, Enum.ToObject(modeType, mode),
                Activator.CreateInstance(palette, primary, secondary))! with { Tick = world.Tick };
        CharacterCold Cold(CharacterId id) => world.Characters.Cold(id.Slot)!;
        int Primary(CharacterId id) => Cold(id).Appearance!.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1");
        void Apply(CharacterId id, int mode, int primary, int secondary) { world.Commands.Enqueue(Edit(id, mode, primary, secondary)); world.RunTick(); }

        var uiType = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        var player = Activator.CreateInstance(uiType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [session, (Func<float, float, float>)((_, _) => 0), (Func<CharacterSnapshot, Vector3>)(c => c.Position)], null)!;
        var screens = new MyGuiScreenHost(new MyGuiResources());
        AccessTools.Field(uiType, "FaithfulScreens").SetValue(player, screens);
        AccessTools.Field(uiType, "FaithfulHud").SetValue(player, new MyGuiLayout("", []));
        AccessTools.Field(uiType, "Width").SetValue(player, 1280);
        AccessTools.Field(uiType, "Height").SetValue(player, 900);
        var router = AccessTools.Property(uiType, "ScreenRouter").GetValue(player)!;
        var routerType = router.GetType();
        var route = AccessTools.Method(routerType, "FaithfulAction");
        screens.Activated += e => route.Invoke(router, [e]);
        UiBindings BindHud() => (UiBindings)AccessTools.Method(uiType, "BindUi").Invoke(player, null)!;
        IReadOnlyList<MyGuiScreenFrame> ArrangeScreens(int width = 1280, int height = 900) => screens.Arrange(width, height,
            id => (UiBindings)AccessTools.Method(routerType, "BindFaithfulScreen").Invoke(router, [id])!,
            (id, controls) => AccessTools.Method(routerType, "AddFaithfulContent").Invoke(router, [id, controls]));
        IReadOnlyList<UiControl> Frame(int width = 1280, int height = 900) => ArrangeScreens(width, height).Single(s => s.Id == Screen).Controls;
        UiControl Control(string name) => Frame().Single(c => c.Name == name);
        void Click(string name)
        {
            var c = Control(name);
            screens.PointerDown(new(c.Bounds.X + c.Bounds.Width / 2, c.Bounds.Y + c.Bounds.Height / 2)); screens.PointerUp();
        }
        void Type(string name, string text)
        {
            Click(name); Check(screens.EditKey(UiEditKey.SelectAll), "native edit box selects text");
            foreach (char value in text) Check(screens.TextInput(value), "native edit box accepts text");
        }
        void Select(CharacterId id)
        {
            world.Commands.Enqueue(new SelectCommand([id]) { Tick = world.Tick }); world.ApplyPausedInterfaceCommands();
        }

        foreach (var uiMode in new[] { UiMode.Faithful, UiMode.Meitou })
        {
            AccessTools.Property(uiType, "UiMode").SetValue(player, uiMode);
            var ui = (UiHost)AccessTools.Field(uiType, "_uiHost").GetValue(player)!;
            var diagnostics = new List<UiDiagnostic>(); ui.Diagnostic += diagnostics.Add;
            var registrations = host.RegisterUi(ui.Register);
            IReadOnlyList<UiControl> Menu()
            {
                object?[] frame = [player, ui.Arrange(uiMode, 1280, 900, BindHud(), new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, 1280, 900) })];
                AccessTools.Method(mod.GetType("CharTweak3.PortraitRendering", true)!, "Arrange").Invoke(null, frame);
                return (IReadOnlyList<UiControl>)frame[1]!;
            }
            void MenuClick(string name) => ui.Dispatch(Menu().Single(c => c.Name == "chartweak3-meitou:" + name));
            void Open(CharacterId id)
            {
                if (screens.IsOpen(Screen)) Click("Cancel");
                Select(id); MenuClick("open");
                Check(Menu().Single(c => c.Name == "chartweak3-meitou:colours").Enabled, "menu offers player colors");
                MenuClick("colours"); Check(screens.IsOpen(Screen) && screens.HasModal && session.Simulation.RequestedPause, "native color modal opens and requests pause");
            }
            Select(first); MenuClick("open");
            Check(BindHud().Text["chartweak3.profile.colours"] == "equipment|CCAA88|334455", "profile reads original equipment colors rather than a default palette");
            Check(Menu().Single(c => c.Name == "chartweak3-meitou:profile-primary").Background == new Vector4(204 / 255f, 170 / 255f, 136 / 255f, 1)
                && Menu().Single(c => c.Name == "chartweak3-meitou:profile-secondary").Background == new Vector4(51 / 255f, 68 / 255f, 85 / 255f, 1),
                "profile swatches render actual primary and secondary RGB");
            var menuBefore = Menu().Single(c => c.Name == "chartweak3-meitou:window").Bounds;
            ArrangeScreens();
            var menuTitle = new Vector2(menuBefore.X + menuBefore.Width / 2, menuBefore.Y + 16);
            Check(screens.PointerDown(menuTitle) && screens.CapturesPointer, "main personality menu uses native title dragging");
            Check(screens.PointerMove(menuTitle + new Vector2(120, -80)), "main menu follows native drag"); screens.PointerUp();
            var menuMoved = Menu().Single(c => c.Name == "chartweak3-meitou:window").Bounds;
            Check(menuMoved.X == menuBefore.X + 120 && menuMoved.Y == menuBefore.Y - 80, "main menu moves with all controls");
            MenuClick("close"); Check(!screens.IsOpen("chartweak3-menu-drag"), "closing menu removes native drag strip");
            MenuClick("open");
            Check(Menu().Single(c => c.Name == "chartweak3-meitou:window").Bounds == menuMoved, "main menu remembers moved position");
            MenuClick("close");
            Open(first);
            Check(!screens.IsOpen("chartweak3-menu-drag"), "opening colors hides main menu drag strip");
            screens.SetOffset(Screen, Vector2.Zero);
            Check(Frame(800, 600).All(c => c.Bounds.X >= 0 && c.Bounds.Y >= 0 && c.Bounds.X + c.Bounds.Width <= 800 && c.Bounds.Y + c.Bounds.Height <= 600),
                "native color dialog fits an 800x600 viewport");
            var windowBefore = Control("ColourWindow").Bounds;
            var title = new Vector2(windowBefore.X + windowBefore.Width / 2, windowBefore.Y + 20);
            Check(screens.PointerDown(title) && screens.CapturesPointer, "title press starts native window drag");
            Check(screens.PointerMove(title + new Vector2(-200, 20)), "native window follows drag"); screens.PointerUp();
            var movedWindow = Control("ColourWindow").Bounds;
            Check(movedWindow.X == windowBefore.X - 200 && movedWindow.Y == windowBefore.Y + 20 && !screens.CapturesPointer,
                "color window moves aside and releases drag capture");
            Type("PrimaryHex", "191919"); Type("SecondaryHex", "B9AA87");
            var launcherVisible = host.Settings.Single(g => g.ModId == "chartweak3-meitou").Settings.Single(s => s.Key == "ShowLauncher");
            object visibilityBeforeTyping = launcherVisible.Get()!;
            session.Input.SetKey(Key.Backslash, true); session.Tick(); session.Input.SetKey(Key.Backslash, false); session.Tick();
            Check(Equals(launcherVisible.Get(), visibilityBeforeTyping), "launcher shortcut ignores focused color text fields");
            Click("PrimarySwatch");
            Check(screens.GetText(Screen, "PrimaryHex") == "DCDCDC" && Control("PrimaryQuick").Caption == "White"
                && screens.GetText(Screen, "SecondaryHex") == "B9AA87", "primary swatch cycles its preset independently");
            Check(Control("PrimarySwatch").HoverColour == Control("PrimarySwatch").Background,
                "swatch retains its actual color under pointer");
            for (int i = 0; i < 10; i++) Click("PrimarySwatch");
            Check(screens.GetText(Screen, "PrimaryHex") == "191919", "primary swatch wraps through all eleven quick colors");
            Click("SecondarySwatch");
            Check(screens.GetText(Screen, "SecondaryHex") == "191919" && Control("SecondaryQuick").Caption == "Black",
                "secondary swatch wraps from Bone to Black");
            Check(Control("ColourWindow").Bounds == movedWindow, "swatch clicks do not drag the window");
            Type("PrimaryHex", "CCAA88"); Type("SecondaryHex", "334455");
            Check(Control("PrimaryR").Properties["BarMax"] == "255", "RGB slider has full byte range");
            Click("PrimaryR@SecondPart");
            Check(Control("PrimaryRValue").Caption != "204", "native slider track changes preview");
            Type("PrimaryHex", "#FF2200"); Type("SecondaryHex", "11CC44");
            Check(Control("PrimaryRValue").Caption == "255" && Control("SecondaryGValue").Caption == "204", "hex entry synchronizes RGB sliders");
            Check(ReferenceEquals(Cold(first).Appearance, original), "draft preview never mutates live appearance");
            Type("PrimaryHex", "broken");
            Check(!Control("Apply").Enabled, "malformed hex disables Apply");
            Type("PrimaryHex", "FF2200");
            string presetName = "Scout " + uiMode;
            Type("PresetName", presetName); Click("SavePreset");
            Check(Control("Preset").Caption == presetName && Control("Status").Caption == "Preset saved.", "named preset saved from native field");
            Click("SavePreset"); Check(Control("Status").Caption.Contains("already exists"), "duplicate names require explicit overwrite");
            var stale = Control("Apply");
            Click("Cancel");
            Check(!screens.HasModal && !session.Simulation.RequestedPause && ReferenceEquals(Cold(first).Appearance, original), "Cancel releases modal and discards preview");
            Open(first);
            Check(Control("ColourWindow").Bounds == movedWindow, "reopening color window retains its moved position");
            route.Invoke(router, [new MyGuiEvent(Screen, stale.Name, stale.Action)]);
            Check(screens.IsOpen(Screen), "stale modal action cannot commit a reopened draft");
            // Select saved preset by its name (the library is shared across HUD modes).
            for (int i = 0; Control("Preset").Caption != presetName && i < 64; i++) Click("PresetNext");
            Click("LoadPreset"); Click("Apply");
            Check(screens.IsOpen(Screen) && ReferenceEquals(Cold(first).Appearance, original), "Apply keeps window open and queues without a UI-thread live write");
            var personalitySystem = world.Systems.Single(s => s.GetType().Name == "PersonalitySystem");
            var personalityCommand = (SimCommand)Activator.CreateInstance(mod.GetType("CharTweak3.SetPersonality", true)!,
                Token(personalitySystem), first, 5)!;
            world.Commands.Enqueue(personalityCommand with { Tick = world.Tick });
            int personalityBeforeApply = Cold(first).Personality;
            long tickBeforeApply = world.Tick;
            Check(session.ApplyPausedCommandsNow() && world.Tick == tickBeforeApply, "colors apply at paused boundary without a gameplay tick");
            Check(BindHud().Text["chartweak3.profile.colours"].StartsWith("personal|FF2200|"), "profile follows applied personal color while paused");
            Check(Cold(first).Personality == personalityBeforeApply
                && world.Commands.TakeDue(world.Tick, c => c.GetType().Name == "SetPersonality").Count == 1,
                "paused color updates leave other custom commands queued");
            Check(screens.IsOpen(Screen) && Control("Status").Caption == "Colors applied." && Control("ColourWindow").Bounds == movedWindow,
                "applied status and position remain visible in open modal");
            Check(session.CurrentSnapshot.Characters.Single(c => c.Id == first).Appearance!.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0xFF2200,
                "paused color update is published to renderer snapshot");
            Check(Primary(first) == 0xFF2200 && Primary(second) == 0xCCAA88, "personal palette isolates identical shared source appearances");
            Check(Cold(first).Appearance!.BodyLayers.Single().Colour!.GetInt("color 2") == 0x11CC44, "shirt secondary follows personal palette");
            Check(!ReferenceEquals(Cold(first).Appearance!.Parts, original.Parts) && !ReferenceEquals(Cold(first).Appearance!.BodyLayers, original.BodyLayers), "published source collections are detached");
            Check(original.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0xCCAA88, "shared source is unchanged");
            Check(Cold(first).Appearance!.Parts.Single(p => p.Record.StringId == "personal-protected").Colour is null, "protected armor keeps native behavior");
            Check(Cold(first).Inventory!.Items.All(i => i.ColourId == dye.StringId), "personal preference does not overwrite tradeable item dyes");
            Type("PrimaryHex", "FF3300"); Click("Apply"); session.ApplyPausedCommandsNow();
            Check(Primary(first) == 0xFF3300 && screens.IsOpen(Screen), "repeated Apply updates colors while modal remains open");
            Type("PrimaryHex", "FF2200"); Click("Apply"); session.ApplyPausedCommandsNow();
            var stableLook = Cold(first).Appearance; world.RunTick(); Check(ReferenceEquals(stableLook, Cold(first).Appearance), "unchanged colors reuse appearance rather than rebuilding every tick");
            var bare = Cold(first).Appearance!.WithoutHat(db, Path.GetTempPath());
            Check(bare.Parts.All(p => p.Slot != AttachSlot.Hat) && bare.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0xFF2200,
                "native hide-hats rebuild retains personal dye");

            Open(first); Click("Uniform"); Type("PrimaryHex", "2244FF"); Type("SecondaryHex", "FFFFFF"); Click("Apply"); session.ApplyPausedCommandsNow();
            Check(BindHud().Text["chartweak3.profile.colours"] == "uniform|2244FF|FFFFFF", "profile identifies uniform colors exactly");
            Open(second); Click("Uniform");
            Check(screens.GetText(Screen, "PrimaryHex") == "2244FF", "another character sees current world uniform");
            Click("Apply"); session.ApplyPausedCommandsNow();
            Check(Primary(first) == 0x2244FF && Primary(second) == 0x2244FF, "characters enroll in shared uniform");
            Open(first); Type("PrimaryHex", "55AA66"); Click("Apply"); session.ApplyPausedCommandsNow();
            Check(Primary(first) == 0x55AA66 && Primary(second) == 0x55AA66 && ReferenceEquals(Cold(untouched).Appearance, original), "uniform update affects members and isolates nonmembers");
            Open(second); Click("Personal"); Type("PrimaryHex", "AABBCC"); Click("Apply"); session.ApplyPausedCommandsNow();
            Apply(first, 2, 0x445566, 0xFFFFFF);
            Check(Primary(first) == 0x445566 && Primary(second) == 0xAABBCC, "Personal leaves uniform membership");

            // Exercise the real native edit command before testing other appearance replacements.
            var editedBody = CharacterDraft.Copy(body); editedBody.Floats["Height"] = 120;
            world.Commands.Enqueue(new EditCharacter(second, editedBody, "Color Beep", EditKind.Recruit) { Tick = world.Tick }); world.RunTick();
            Check(Primary(second) == 0xAABBCC && Cold(second).Appearance!.Body!.Get("Height") == 120,
                "native character edit applies new body and retains personal palette");
            // Equipment/body systems can also supply a fresh look; retain all its metadata.
            var newLook = Build(Cold(second).Inventory!); newLook.Shape = new CharacterShapeInput();
            Cold(second).Appearance = newLook; world.RunTick();
            Check(Primary(second) == 0xAABBCC && Cold(second).Appearance!.Shape == newLook.Shape, "appearance replacements keep palette and new body metadata");
            Open(second); Click("Reset"); session.ApplyPausedCommandsNow();
            Check(ReferenceEquals(Cold(second).Appearance, newLook), "Reset restores latest original appearance, including native item colors");
            Check(BindHud().Text["chartweak3.profile.colours"] == "equipment|CCAA88|334455", "profile returns to native equipment colors after reset");
            Check(screens.IsOpen(Screen) && Control("Status").Caption == "Original colors restored."
                && screens.GetText(Screen, "PrimaryHex") == "CCAA88", "Reset keeps window open and refreshes native color fields");
            Open(first); Click("Reset"); session.ApplyPausedCommandsNow();
            Check(ReferenceEquals(Cold(first).Appearance, original), "Reset restores original shared source without touching others");
            Click("Cancel");
            world.Commands.Enqueue(new InspectCommand(npc) { Tick = world.Tick }); world.ApplyPausedInterfaceCommands(); MenuClick("open");
            Check(!Menu().Single(c => c.Name == "chartweak3-meitou:colours").Enabled, "NPC colors are disabled");
            Check(diagnostics.Count == 0, "color menu has no UI diagnostics");
            foreach (var registration in registrations) registration.Dispose();
            Console.WriteLine($"PASS: {uiMode} draggable menus, swatch/RGB/hex controls, open-window paused apply/reset, isolated colors, uniforms, native edits and hide hats.");
        }

        Apply(first, 2, 0x123456, 0xABCDEF); Apply(second, 2, 0x123456, 0xABCDEF);
        Apply(untouched, 1, 0xDEADBE, 0xEFCAFE);
        var save = SaveCapture.Capture(world, data, new SaveClock(1, 10, 0));
        var sidecars = new Dictionary<string, byte[]>(); host.CaptureSaveData(world, sidecars);
        Check(sidecars.ContainsKey("chartweak3-meitou"), "real ModHost captures uniform sidecar");
        string directory = Path.Combine(Path.GetTempPath(), "chartweak3-colour-save-" + Guid.NewGuid().ToString("N"));
        SaveGame disk;
        try { save.Write(directory); disk = SaveGame.Load(SaveFolder.Open(directory)); }
        finally { Directory.Delete(directory, recursive: true); }
        using var loaded = new World(new WorldSettings { Seed = 2 }, new OpenGroundWalkability((_, _) => 0), Systems());
        var loadedSave = SaveLoader.Load(loaded, disk, data, new SaveLoadOptions { Appearances = data.Appearances });
        host.RestoreSaveData(loaded, sidecars); loaded.TellReady(true);
        CharacterCold Loaded(string name) => Enumerable.Range(0, loaded.Characters.HighWater).Select(loaded.Characters.Cold).Single(c => c?.Name == name)!;
        Check(Loaded("Color Pin").Appearance!.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0x123456,
            "native save/load restores member palette before first gameplay tick");
        Check(Loaded("Color Unchanged").Appearance!.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0xDEADBE,
            "personal colors restore independently alongside uniforms");
        var loadedColours = loaded.Systems.Single(s => s.GetType().Name == "ColourSystem");
        var loadedFirst = Enumerable.Range(0, loaded.Characters.HighWater).Select(loaded.Characters.IdOf).Single(id => loaded.Characters.Cold(id.Slot)?.Name == "Color Pin");
        var update = Edit(loadedFirst, 2, 0x778899, 0x102030, Token(loadedColours));
        loaded.Commands.Enqueue(update with { Tick = loaded.Tick }); loaded.RunTick();
        Check(Loaded("Color Beep").Appearance!.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0x778899,
            "uniform membership survives native character load and sidecar restore");
        var saveAgain = SaveCapture.Capture(loaded, data, new SaveClock(1, 10, 0), loadedSave);
        Check(saveAgain.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Color Pin").GameState!.Ints["chartweak3.colour.primary"] == 0x778899,
            "repeated save contains latest effective uniform colors");
        var before = loaded.StateHash();
        loadedColours.Inputs(loaded, [Edit(loadedFirst, 1, 0xFFFFFF, 0)]); loadedColours.SlowWorld(loaded);
        Check(loaded.StateHash() == before, "stale world color command is rejected");
        Apply(first, 0, 0, 0);
        var resetSave = SaveCapture.Capture(world, data, new SaveClock(1, 10, 0));
        Check(!resetSave.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Color Pin").GameState!.Ints.ContainsKey("chartweak3.colour.mode"), "reset removes preference from native character save");
        foreach (var invalid in new[] { Edit(first, 9, 0, 0), Edit(first, 1, -1, 0), Edit(first, 1, 0x1000000, 0), Edit(npc, 1, 1, 2) })
            colours.Inputs(world, [invalid]);
        colours.SlowWorld(world);
        Check(ReferenceEquals(Cold(first).Appearance, original) && ReferenceEquals(Cold(npc).Appearance, original), "invalid colors, modes and NPC targets refused");
        var removed = Spawn("Color Removed"); world.Characters.Remove(removed); var replacement = Spawn("Color Replacement");
        Check(replacement.Slot == removed.Slot && replacement.Generation != removed.Generation, "color fixture reuses removed slot");
        colours.Inputs(world, [Edit(removed, 1, 0xFFFFFF, 0)]); colours.SlowWorld(world);
        Check(ReferenceEquals(Cold(replacement).Appearance, original), "removed-generation color command refused");
        Cold(replacement).Medical = new MedicalState([]); Cold(replacement).Medical!.Kill();
        colours.Inputs(world, [Edit(replacement, 1, 0xFFFFFF, 0)]); colours.SlowWorld(world);
        Check(ReferenceEquals(Cold(replacement).Appearance, original), "dead target color command refused");
        var h1 = new StateHasher(); ((IStateHashed)colours).Hash(ref h1);
        Apply(first, 1, 0x110022, 0x334455);
        var h2 = new StateHasher(); ((IStateHashed)colours).Hash(ref h2);
        Check(h1.Value != h2.Value, "personal color state participates in hash");
        using (var fallback = new World(new WorldSettings { Seed = 3 }, new OpenGroundWalkability((_, _) => 0), Systems()))
        {
            SaveLoader.Load(fallback, disk, data, new SaveLoadOptions { Appearances = data.Appearances }); fallback.TellReady(true);
            var member = Enumerable.Range(0, fallback.Characters.HighWater).Select(fallback.Characters.Cold).Single(c => c?.Name == "Color Pin")!;
            Check(member.Appearance!.Parts.Single(p => p.Record.StringId == "personal-shirt").Colour!.GetInt("color 1") == 0x123456,
                "native character fields restore uniform if import omits sidecar");
        }
        ulong DeterministicHash(int threads)
        {
            using var test = new World(new WorldSettings { Seed = 42, Threads = threads, MinPartitionSize = 1 }, new OpenGroundWalkability((_, _) => 0), Systems());
            var id = test.Characters.Spawn(new CharacterHot { Health = 100 }, new CharacterCold { Name = "Deterministic color", RecordId = hero.StringId, IsPlayer = true, Appearance = original }, 0);
            test.TellReady(false);
            var cs = test.Systems.Single(s => s.GetType().Name == "ColourSystem");
            test.Commands.Enqueue(Edit(id, 1, 0x112233, 0x445566, Token(cs)) with { Tick = test.Tick }); test.RunTicks(2);
            return test.StateHash();
        }
        Check(DeterministicHash(1) == DeterministicHash(4), "colors hash identically across thread counts");
        Console.WriteLine("PASS: real native color saves on disk, ModHost uniform sidecars, loaded membership, repeated saves, reset and invalid/stale guards.");
        PaletteLibraryChecks.Run(mod);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
