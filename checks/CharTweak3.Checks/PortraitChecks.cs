using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.Textures;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Items;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;

internal static class PortraitChecks
{
    internal static void Run(ModHost host, GameDatabase db, Action<World> useWorld)
    {
        var mod = host.Mods.Single().Assemblies.Single();
        Type Type(string name) => mod.GetType("CharTweak3." + name, true)!;
        object? Call(Type type, string method, object? instance, params object?[] args) => AccessTools.Method(type, method).Invoke(instance, args);
        object? Property(object instance, string name) => AccessTools.Property(instance.GetType(), name).GetValue(instance);
        var assetType = Type("PortraitAsset"); var filesType = Type("PortraitFiles");
        var messages = new List<string>();
        object files = Activator.CreateInstance(filesType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [(Action<string>)messages.Add], null)!;
        var pickerType = Type("PortraitPicker");
        var windowsStruct = pickerType.GetNestedType("OpenFileName", BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(windowsStruct) == (IntPtr.Size == 8 ? 152 : 88), "Windows OPENFILENAMEW ABI size matches pointer architecture");
        Check(Marshal.OffsetOf(windowsStruct, "File").ToInt32() == (IntPtr.Size == 8 ? 48 : 28)
            && Marshal.OffsetOf(windowsStruct, "Flags").ToInt32() == (IntPtr.Size == 8 ? 96 : 52), "Windows buffer and flags fields have native offsets");
        string temporary = Path.Combine(Path.GetTempPath(), "chartweak3-portraits-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        string? oldPath = Environment.GetEnvironmentVariable("PATH"), oldDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        try
        {
            string source = Path.Combine(temporary, "Pin's portrait ; $(safe) ü.bmp");
            File.WriteAllBytes(source, Bitmap(640, 320));
            object asset = Call(filesType, "Import", files, source)!;
            string id = (string)Property(asset, "Id")!, cache = (string)Property(asset, "Path")!;
            Check((int)Property(asset, "Width")! == 512 && (int)Property(asset, "Height")! == 256, "import resizes proportionally to 512 pixels");
            var decoded = TextureLoader.LoadFile(cache).Levels.Single();
            Check(decoded[0, 0].R == 240 && decoded[0, 255].B == 220 && decoded[0, 0].A == 192, "normalized PNG preserves orientation, RGBA and alpha");
            object same = Call(filesType, "Import", files, cache)!;
            Check((string)Property(same, "Id")! == id, "normalized imports deduplicate by content");
            string invalid = Path.Combine(temporary, "invalid.png"); File.WriteAllText(invalid, "not an image");
            ExpectFailure(() => Call(filesType, "Import", files, invalid), "invalid image rejected");
            string huge = Path.Combine(temporary, "huge.bmp"); var hugeBytes = Bitmap(1, 1); BitConverter.GetBytes(100000).CopyTo(hugeBytes, 18); File.WriteAllBytes(huge, hugeBytes);
            ExpectFailure(() => Call(filesType, "Import", files, huge), "oversized dimensions rejected before allocation");
            var bad = ((byte[])Property(asset, "Png")!).ToArray(); bad[^1] ^= 1;
            ExpectFailure(() => Call(filesType, "Validate", null, id, bad, ""), "changed embedded image hash rejected");
            Check(Call(filesType, "Find", files, "../../outside") is null, "portrait identities cannot escape storage");

            var records = new FcsFile();
            var race = new FcsRecord { StringId = "portrait-race", RecordType = FcsRecordType.Race };
            var hero = new FcsRecord { StringId = "portrait-hero", RecordType = FcsRecordType.Character };
            hero.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)]; records.Records.AddRange([race, hero]); db.Apply(records, "portraits-check.mod");
            var data = PopulationData.Create(db, [], new GeneratedAppearances(db, Path.GetTempPath()));
            var body = new FcsRecord { StringId = "portrait-body", RecordType = FcsRecordType.CharacterAppearance };
            body.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)]; body.Floats["Height"] = 100;
            var look = CharacterAppearance.Build(db, Path.GetTempPath(), hero.StringId,
                new CharacterOptions { Loadout = SavedKit.LoadoutOf(body, [], db, 1) });
            ITickSystem[] Systems() => [new PlayerSystem(data), .. host.Simulation!.Systems.Select(f => f.Create(new SystemBuildContext(data, []))!)];
            using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: Systems());
            var world = session.World; useWorld(world); world.Player.Faction = data.PlayerFaction;
            session.Simulation.PauseKey();
            Check(session.Simulation.IsPaused, "portrait test clock is paused");
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "portrait", Faction = data.PlayerFaction }; world.Squads.Add(squad);
            CharacterId Spawn(string name, bool player = true)
            {
                var result = world.Characters.Spawn(new CharacterHot { Health = 100 }, new CharacterCold
                { Name = name, RecordId = hero.StringId, IsPlayer = player, Faction = squad.Faction, SquadId = squad.Id, Appearance = look, Inventory = new Inventory() }, 0);
                squad.Members.Add(result); if (squad.Leader.IsNone) squad.Leader = result; return result;
            }
            var first = Spawn("Portrait Pin"); var second = Spawn("Portrait Beep"); var npc = Spawn("Portrait NPC", false);
            world.Player.Selection.Add(first); world.TellReady(false); world.RunTick();
            var system = world.Systems.Single(s => s.GetType().Name == "PortraitSystem");
            string Token(ITickSystem s) => (string)Property(s, "Session")!;
            SimCommand Command(CharacterId character, object? image, string? token = null) =>
                (SimCommand)Activator.CreateInstance(Type("SetPortrait"), token ?? Token(system), character, image)! with { Tick = world.Tick };
            IDictionary View(ITickSystem s) => (IDictionary)Property(s, "View")!;
            string? Assigned(CharacterId character) => (string?)Call(Type("PortraitSave"), "Get", null, world.Characters.Cold(character.Slot));

            var uiType = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
            var playerUi = Activator.CreateInstance(uiType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [session, (Func<float, float, float>)((_, _) => 0), (Func<CharacterSnapshot, Vector3>)(c => c.Position)], null)!;
            var screens = new MyGuiScreenHost(new MyGuiResources());
            AccessTools.Field(uiType, "FaithfulScreens").SetValue(playerUi, screens);
            AccessTools.Field(uiType, "FaithfulHud").SetValue(playerUi, new MyGuiLayout("", []));
            AccessTools.Field(uiType, "Width").SetValue(playerUi, 1280); AccessTools.Field(uiType, "Height").SetValue(playerUi, 900);
            UiBindings Bind() => (UiBindings)AccessTools.Method(uiType, "BindUi").Invoke(playerUi, null)!;
            var ui = (UiHost)AccessTools.Field(uiType, "_uiHost").GetValue(playerUi)!;
            void Select(CharacterId character) { world.Commands.Enqueue(new SelectCommand([character]) { Tick = world.Tick }); world.ApplyPausedInterfaceCommands(); }
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("These picker process tests use Linux fixtures.");
            string zenity = Path.Combine(temporary, "zenity"), kdialog = Path.Combine(temporary, "kdialog");
            void Tool(string path, string script)
            { File.WriteAllText(path, "#!/bin/sh\n" + script); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            Tool(zenity, "printf '%s\\n' \"$CHARTWEAK_TEST_PICK\"\n"); Tool(kdialog, "printf '%s\\n' \"$CHARTWEAK_TEST_PICK\"\n");
            Environment.SetEnvironmentVariable("PATH", temporary); Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "GNOME");
            Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", source);
            Task<string?> Pick() => (Task<string?>)Call(pickerType, "Choose", null)!;
            Check(Pick().GetAwaiter().GetResult() == source, "Linux chooser preserves Unicode, quotes and shell metacharacters as path data");
            Tool(zenity, "exit 1\n"); Check(Pick().GetAwaiter().GetResult() is null, "Cancel does not open fallback chooser");
            File.Delete(zenity); Check(Pick().GetAwaiter().GetResult() == source, "missing Zenity falls back to KDE chooser");
            Tool(zenity, "printf '%s\\n' \"$CHARTWEAK_TEST_PICK\"\n");

            foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
            {
                AccessTools.Property(uiType, "UiMode").SetValue(playerUi, mode);
                var registrations = host.RegisterUi(ui.Register);
                IReadOnlyList<UiControl> Menu() => ui.Arrange(mode, 1280, 900, Bind(), new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, 1280, 900) });
                UiControl Control(string name) => Menu().Single(c => c.Name == "chartweak3-meitou:" + name);
                void Click(string name) => ui.Dispatch(Control(name));
                void FinishImport()
                {
                    var wait = Stopwatch.StartNew();
                    while (wait.Elapsed < TimeSpan.FromSeconds(5))
                    { Menu(); if (!(bool)AccessTools.Property(pickerType, "IsOpen").GetValue(null)! && !Control("portrait").Caption.Contains("Importing")) return;
                        session.ApplyPausedCommandsNow(); Thread.Sleep(10); }
                    throw new Exception("Portrait import did not finish: " + Control("status").Caption);
                }
                Select(first); Click("open"); Check(Control("portrait").Enabled, "player menu enables portrait chooser");
                Click("portrait"); FinishImport(); session.ApplyPausedCommandsNow(); Menu();
                Check(Assigned(first) == id && Assigned(second) is null, "same-template characters keep independent portraits");
                Check(Control("status").Caption == "Portrait applied." && Control("portrait-reset").Enabled, "menu acknowledges paused apply and stays open");
                Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", invalid); Click("portrait"); FinishImport();
                Check(Assigned(first) == id, "invalid image import retains the previous assignment");
                Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", source);
                var before = world.Tick; Click("portrait-reset"); session.ApplyPausedCommandsNow();
                Check(Control("status").Caption == "Original portrait restored." && Assigned(first) is null && world.Tick == before, "Reset keeps menu open and does not advance time");
                Click("next"); Check(Control("status").Caption == "Press Apply to change personality.", "later personality actions replace portrait feedback");
                // Hold the process until selection changes, without timing assumptions about worker scheduling.
                string release = Path.Combine(temporary, "release"); Environment.SetEnvironmentVariable("CHARTWEAK_TEST_RELEASE", release);
                Tool(zenity, "while [ ! -e \"$CHARTWEAK_TEST_RELEASE\" ]; do /usr/bin/sleep 0.01; done\nprintf '%s\\n' \"$CHARTWEAK_TEST_PICK\"\n");
                Click("portrait"); Select(second); File.WriteAllText(release, "go"); FinishImport();
                Check(Assigned(first) is null && Assigned(second) is null, "selection changes while choosing never assign to either character"); File.Delete(release);
                Tool(zenity, "exit 1\n"); Click("portrait"); FinishImport(); Check(Assigned(second) is null, "cancel leaves assignments intact");
                world.Commands.Enqueue(new InspectCommand(npc) { Tick = world.Tick }); world.ApplyPausedInterfaceCommands();
                Check(!Control("portrait").Enabled, "NPC portrait chooser disabled");
                Click("close"); foreach (var registration in registrations) registration.Dispose();
                Tool(zenity, "printf '%s\\n' \"$CHARTWEAK_TEST_PICK\"\n");
                Console.WriteLine($"PASS: {mode} async portrait chooser, paused apply/reset, cancellation, selection change and character isolation.");
            }

            world.Commands.Enqueue(Command(first, asset)); world.Commands.Enqueue(Command(second, asset));
            world.ApplyPausedInterfaceCommands();
            Check(View(system).Count == 2, "portrait publication covers unselected squad members");
            // Exercise the same postfix Harmony applies to ArrangeUi using its actual native atlas mapping.
            var bar = AccessTools.Property(uiType, "PortraitBar").GetValue(playerUi)!;
            string Rect(int cell) => (string)AccessTools.Method(bar.GetType(), "PortraitRect").Invoke(bar, [cell])!;
            var control = new UiControl("portrait-0", "ImageBox", new(10, 20, 120, 120), "", "", "", true,
                new Dictionary<string, string> { ["ImageTexture"] = "meitou:portraits", ["ImageRect"] = Rect(0), ["ImageFlipV"] = "true", ["Alpha"] = "0.25" }, Vector4.Zero, Clip: new(0, 0, 500, 500));
            IReadOnlyList<UiControl> controls = [control, control with { Name = "record-portrait", Properties = new Dictionary<string, string>(control.Properties) { ["ImageRect"] = Rect(40) } }];
            object?[] arranged = [playerUi, controls]; Call(Type("PortraitRendering"), "Arrange", null, arranged);
            var rendered = (IReadOnlyList<UiControl>)arranged[1]!;
            Check(rendered[0].Properties["ImageTexture"] == "file:" + cache && !rendered[0].Properties.ContainsKey("ImageFlipV")
                && rendered[0].Properties["Alpha"] == "0.25" && rendered[0].Bounds == new UiRect(10, 50, 120, 60) && rendered[0].Clip == control.Clip,
                "portrait renderer fits without stretching and retains native fade/clipping");
            Check(ReferenceEquals(rendered[1], controls[1]) && controls[0].Properties["ImageTexture"] == "meitou:portraits", "record portraits and cached native controls stay untouched");

            var save = SaveCapture.Capture(world, data, new SaveClock(1, 10, 0)); host.CaptureSaveData(world, save.ModData);
            string saveDirectory = Path.Combine(temporary, "save"); save.Write(saveDirectory); var disk = SaveGame.Load(SaveFolder.Open(saveDirectory));
            File.Delete(source); File.Delete(cache); // Simulate moving the full save to a machine with an empty local cache.
            using var loaded = new World(new WorldSettings { Seed = 2 }, new OpenGroundWalkability((_, _) => 0), Systems());
            var loadedSave = SaveLoader.Load(loaded, disk, data, new SaveLoadOptions { Appearances = data.Appearances }); host.RestoreSaveData(loaded, disk.ModData); loaded.TellReady(true);
            var loadedSystem = loaded.Systems.Single(s => s.GetType().Name == "PortraitSystem");
            Check(View(loadedSystem).Count == 2 && File.Exists(cache), "full disk save restores characters and embedded images without source or cache");
            var repeated = SaveCapture.Capture(loaded, data, new SaveClock(1, 10, 0), loadedSave); host.CaptureSaveData(loaded, repeated.ModData);
            Check(repeated.ModData["chartweak3-meitou"].SequenceEqual(disk.ModData["chartweak3-meitou"]), "repeated saves preserve the exact embedded image bundle");
            var hash = loaded.StateHash(); loadedSystem.Inputs(loaded, [Command(first, null)]); loadedSystem.SlowWorld(loaded);
            Check(loaded.StateHash() == hash, "stale-world portrait commands rejected");
            world.Commands.Enqueue(Command(first, null)); world.ApplyPausedInterfaceCommands();
            var resetSave = SaveCapture.Capture(world, data, new SaveClock(1, 10, 0));
            Check(!resetSave.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Portrait Pin").GameState!.Strings.ContainsKey("chartweak3.portrait.v1"), "Reset removes native character assignment");
            var invalidBefore = world.StateHash(); system.Inputs(world, [Command(npc, asset), Command(first, asset, "wrong-world")]); system.SlowWorld(world);
            Check(world.StateHash() == invalidBefore, "NPC and invalid session commands rejected");
            var removed = Spawn("Removed"); world.Characters.Remove(removed); var replacement = Spawn("Replacement");
            system.Inputs(world, [Command(removed, asset)]); system.SlowWorld(world); Check(Assigned(replacement) is null, "reused character slots reject old generation");
            world.Characters.Cold(replacement.Slot)!.Medical = new MedicalState([]); world.Characters.Cold(replacement.Slot)!.Medical!.Kill();
            system.Inputs(world, [Command(replacement, asset)]); system.SlowWorld(world); Check(Assigned(replacement) is null, "dead characters reject edits");
            using var binary = new MemoryStream(); ((IModSaveData)system).Write(binary); byte[] corrupt = binary.ToArray(); corrupt[^1] ^= 1;
            ExpectFailure(() => ((IModSaveData)system).Read(new MemoryStream(corrupt)), "corrupt save images rejected");
            Console.WriteLine("PASS: portrait image normalization, native character save fields, portable embedded images, repeated saves, rendering and stale/dead/reused guards.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath); Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", oldDesktop);
            Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", null); Environment.SetEnvironmentVariable("CHARTWEAK_TEST_RELEASE", null);
            Directory.Delete(temporary, recursive: true);
        }
    }
    private static byte[] Bitmap(int width, int height)
    {
        var pixels = new byte[54 + width * height * 4];
        using var writer = new BinaryWriter(new MemoryStream(pixels));
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(pixels.Length); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)32);
        writer.Write(0); writer.Write(width * height * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { writer.Write((byte)(y < height / 2 ? 220 : 0)); writer.Write((byte)40); writer.Write((byte)(y >= height / 2 ? 240 : 0)); writer.Write((byte)192); }
        return pixels;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void ExpectFailure(Action action, string message)
    { try { action(); } catch (Exception) { return; } throw new Exception(message); }
}
