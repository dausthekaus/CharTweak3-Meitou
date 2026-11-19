using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Items;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;

string folder = Path.GetFullPath(args[0]);
var db = new GameDatabase();
var records = new FcsFile();
records.Records.Add(new FcsRecord { StringId = "204-gamedata.base", Name = "Nameless", RecordType = FcsRecordType.Faction });
records.Records.Add(new FcsRecord { StringId = "neutral", Name = "Neutral", RecordType = FcsRecordType.Faction });
records.Records.Add(new FcsRecord { StringId = "hero", Name = "Hero", RecordType = FcsRecordType.Character });
db.Apply(records, "check.mod");
var data = PopulationData.Create(db, []);
World? current = null;
var manifest = CodeModManifest.Read(Path.Combine(folder, "meitou.json"));
var info = new ModInfo("CharTweak3-Meitou", Path.Combine(folder, "CharTweak3-Meitou.mod"), ModSource.Local);
var host = ModHost.Load(new ModHostOptions([info], db, data, () => current, GameVersion: new CodeModVersion(0,22,1)));
Assert(host.Mods.Count == 1 && host.Notices.Count == 0, "real ModHost loads beta and installs patches");
var mod = host.Mods[0].Assemblies.Single();
Assert(mod.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0] == manifest.Version!.ToString(), "project/manifest beta versions match");
var system = host.Simulation!.Systems.Single().Create(new SystemBuildContext(data, []))!;
using var session = new WorldSession(Vector3.Zero, (-100,-100,100,100), (_,_) => 0,
    simulation: new WorldSettings { Seed = 918273 }, systems: [new PlayerSystem(), system]);
current = session.World;
var world = current;
world.Player.Faction = data.PlayerFaction;
var pin = Spawn(world, "Pin", true);
var beep = Spawn(world, "Beep", true);
var npc = Spawn(world, "Guard", false);
world.Player.Selection.Add(pin);
world.TellReady(false);
world.RunTick();
var pinCold = world.Characters.Cold(pin.Slot)!;
var beepCold = world.Characters.Cold(beep.Slot)!;
Assert(pinCold.Personality == 0, "reading UI does not roll or mutate vanilla personality");

var playerUiType = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
var playerUi = Activator.CreateInstance(playerUiType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
    [session, (Func<float,float,float>)((_,_) => 0), (Func<CharacterSnapshot, Vector3>)(c => c.Position)], null)!;
AccessTools.Field(playerUiType, "FaithfulScreens").SetValue(playerUi, new MyGuiScreenHost(new MyGuiResources()));
AccessTools.Field(playerUiType, "FaithfulHud").SetValue(playerUi, new MyGuiLayout("", []));
AccessTools.Field(playerUiType, "Width").SetValue(playerUi, 800);
AccessTools.Field(playerUiType, "Height").SetValue(playerUi, 600);
UiBindings Bind() => (UiBindings)AccessTools.Method(playerUiType, "BindUi").Invoke(playerUi, null)!;
var initialBindings = Bind();
Assert(initialBindings.Text["chartweak3.name"] == "Pin" && initialBindings.Text.ContainsKey("chartweak3.target"), "real HUD BindUi postfix publishes selected character");

foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
{
    var ui = new UiHost();
    var diagnostics = new List<UiDiagnostic>();
    ui.Diagnostic += diagnostics.Add;
    var registrations = host.RegisterUi(ui.Register);
    IReadOnlyList<UiControl> Frame(int width = 800, int height = 600) => ui.Arrange(mode, width, height, Bind(),
        new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0,0,width,height) });
    UiControl Control(string name) => Frame().Single(c => c.Name == "chartweak3-meitou:" + name);
    void Click(string name) => ui.Dispatch(Control(name));
    Assert(Frame().All(c => c.Name != "chartweak3-meitou:window"), "panel starts closed");
    Click("open");
    Assert(Control("name").Caption == "Pin", "selector shows HUD character");
    Assert(!Control("apply").Enabled, "unchanged preview cannot apply");
    Assert(MyGuiPresentation.HitTest(Frame(), 790, 590) is null, "empty overlay passes clicks through");
    int[] tags = [1,2,5,6,9,10,11,12,13,14];
    string[] labels = ["Honorable","Traitorous","Smart","Dumb","Brave","Fearful","Warm / Kind","Cold / Cruel","Normal","Maniacal"];
    for (int i = 0; i < tags.Length; i++)
    {
        for (int step = 0; Control("choice").Caption != labels[i] && step < 11; step++) Click("next");
        Assert(Control("choice").Caption == labels[i], "named choices cycle correctly");
        if (Control("apply").Enabled)
        {
            int old = pinCold.Personality;
            Click("apply");
            Assert(pinCold.Personality == old, "UI action queues without mutating live state");
            world.ApplyPausedInterfaceCommands();
            Assert(pinCold.Personality == old, "paused game keeps custom command pending");
            Assert(Control("status").Caption.Contains("Unpause"), "queued feedback explains pause");
            world.RunTick();
        }
        Assert(pinCold.Personality == tags[i], "correct sparse numeric tag applied");
        Assert(Control("current").Caption == "Current: " + labels[i], "current value refreshes after simulation");
        Assert(Personality.Of(world, db, pin, pinCold, null) == tags[i], "dialogue reads the edited personality");
        Assert(beepCold.Personality == 0, "other selected characters are unaffected");
    }
    world.Player.Selection.Clear();
    world.Player.Selection.Add(beep);
    world.ApplyPausedInterfaceCommands();
    // Publish selection through the engine's paused SelectCommand path.
    world.Commands.Enqueue(new SelectCommand([beep], false) { Tick = world.Tick });
    world.ApplyPausedInterfaceCommands();
    Assert(Control("name").Caption == "Beep", "changing selection while paused refreshes the target");
    Assert(!Control("apply").Enabled, "character switch discards the previous preview");
    world.Commands.Enqueue(new InspectCommand(npc) { Tick = world.Tick });
    world.ApplyPausedInterfaceCommands();
    Assert(Control("name").Caption == "Guard", "inspected NPC matches HUD priority over player selection");
    world.Commands.Enqueue(new InspectCommand(CharacterId.None) { Tick = world.Tick });
    world.Commands.Enqueue(new SelectCommand([], false) { Tick = world.Tick, Clear = true });
    world.ApplyPausedInterfaceCommands();
    Assert(!Control("next").Enabled && !Control("apply").Enabled, "no selection disables edits");
    Click("close");
    Assert(Frame().All(c => c.Name != "chartweak3-meitou:window"), "Close dismisses the selector");
    world.Commands.Enqueue(new SelectCommand([pin], false) { Tick = world.Tick });
    world.ApplyPausedInterfaceCommands();
    Click("open");
    var small = Frame(320,240);
    Assert(small.All(c => c.Bounds.X >= 0 && c.Bounds.Y >= 0 && c.Bounds.X + c.Bounds.Width <= 320 && c.Bounds.Y + c.Bounds.Height <= 240), "small viewport bounds");
    foreach (var registration in registrations) registration.Dispose();
    Assert(diagnostics.Count == 0, "no UI diagnostics");
    Console.WriteLine($"PASS: {mode} real UI bridge, preview/apply/close, ten tags, paused selection, NPC target and safe queued edits.");
}

EditorChecks.Run(host, data, w => current = w);
current = world;

// Real capture/load round trip, including an untouched character and existing dialogue locks.
pinCold.DialogueLocks = new(StringComparer.Ordinal) { ["example-dialogue"] = true };
var saved = SaveCapture.Capture(world, data, new SaveClock(1, 10, 0));
var pinRecord = saved.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Pin").GameState!;
var beepRecord = saved.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Beep").GameState!;
Assert(pinRecord.Ints["personality"] == 14, "real SaveCapture writes edited personality");
Assert(beepRecord.Ints.GetValueOrDefault("personality") == 0, "unmodified natural personality is not written");
var secondSystem = host.Simulation.Systems.Single().Create(new SystemBuildContext(data, []))!;
using var loaded = new World(new WorldSettings { Seed = 1 }, new OpenGroundWalkability((_,_) => 0), [new PlayerSystem(), secondSystem]);
string saveDirectory = Path.Combine(Path.GetTempPath(), "chartweak3-save-" + Guid.NewGuid().ToString("N"));
saved.Write(saveDirectory);
SaveGame diskSave;
try
{
    diskSave = SaveGame.Load(SaveFolder.Open(saveDirectory));
}
finally
{
    Directory.Delete(saveDirectory, recursive: true);
}
var loadedSave = SaveLoader.Load(loaded, diskSave, data);
loaded.TellReady(true);
var loadedPin = Enumerable.Range(0, loaded.Characters.HighWater).Select(loaded.Characters.Cold).Single(c => c?.Name == "Pin")!;
Assert(loadedPin.Personality == 14, "real SaveLoader restores the edited tag before the first tick");
Assert(loadedPin.DialogueLocks?["example-dialogue"] == true, "dialogue locks survive save hooks");
var savedAgain = SaveCapture.Capture(loaded, data, new SaveClock(1,10,0), loadedSave);
Assert(savedAgain.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Pin").GameState!.Ints["personality"] == 14, "second save retains the edit after loading");
Console.WriteLine("PASS: real SaveCapture/SaveLoader round trip, repeated saves, untouched-character isolation and dialogue lock preservation.");

// Commands from an old world must not affect a new world with reused slot/generation ids.
var commandType = mod.GetType("CharTweak3.SetPersonality", true)!;
var oldSession = (string)system.GetType().GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system)!;
var stale = (SimCommand)Activator.CreateInstance(commandType, oldSession, new CharacterId(0,1), 1)!;
stale = stale with { Tick = loaded.Tick };
int before = loadedPin.Personality;
secondSystem.Inputs(loaded, [stale]);
Assert(loadedPin.Personality == before, "stale world command refused");
var hasher = new StateHasher();
((IStateHashed)system).Hash(ref hasher);
ulong originalHash = hasher.Value;
pinCold.Personality = 1;
var changedHasher = new StateHasher();
((IStateHashed)system).Hash(ref changedHasher);
Assert(originalHash != changedHasher.Value, "edited personalities participate in deterministic hash");
Console.WriteLine("PASS: cross-world command rejection and explicit personality state hashing.");
// Invalid tags, removed/reused character slots, and dead targets cannot be edited.
var thirdSystem = host.Simulation.Systems.Single().Create(new SystemBuildContext(data, []))!;
using var guards = new World(new WorldSettings { Seed = 2 }, new OpenGroundWalkability((_,_) => 0), [thirdSystem]);
var removed = Spawn(guards, "Removed", true);
guards.TellReady(false);
string thirdSession = (string)thirdSystem.GetType().GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(thirdSystem)!;
guards.Characters.Remove(removed);
var replacement = Spawn(guards, "Replacement", true);
Assert(replacement.Slot == removed.Slot && replacement.Generation != removed.Generation, "fixture reuses a slot with a new generation");
foreach (var (id, tag) in new[] { (removed, 1), (replacement, 0), (replacement, 3), (replacement, 15), (CharacterId.None, 1) })
{
    var invalid = (SimCommand)Activator.CreateInstance(commandType, thirdSession, id, tag)!;
    thirdSystem.Inputs(guards, [invalid with { Tick = guards.Tick }]);
}
Assert(guards.Characters.Cold(replacement.Slot)!.Personality == 0, "invalid tags and stale generations refused");
var replacementCold = guards.Characters.Cold(replacement.Slot)!;
replacementCold.Medical = new MedicalState([]);
replacementCold.Medical.Kill();
var deadEdit = (SimCommand)Activator.CreateInstance(commandType, thirdSession, replacement, 1)!;
thirdSystem.Inputs(guards, [deadEdit with { Tick = guards.Tick }]);
Assert(replacementCold.Personality == 0, "dead character edits refused");
Console.WriteLine("PASS: invalid tags, dead characters and removed/reused character slots refused.");
Assert(DeterministicHash(1) == DeterministicHash(4), "same edits hash identically with one and four workers");
Console.WriteLine("PASS: deterministic edit state across thread counts.");
Console.WriteLine("ALL BETA CHECKS PASSED.");

CharacterId Spawn(World w, string name, bool player)
{
    var squad = new Squad { Id = w.Squads.NextId(), TemplateId = "check", Faction = player ? data.PlayerFaction : data.FactionIndex("neutral"), HomeCentre = Vector2.Zero, HomeRadius = 10 };
    w.Squads.Add(squad);
    var cold = new CharacterCold { Name = name, RecordId = "hero", IsPlayer = player, Faction = squad.Faction, SquadId = squad.Id, Inventory = new Inventory() };
    var id = w.Characters.Spawn(new CharacterHot { Health = 100 }, cold, 0);
    squad.Members.Add(id);
    squad.Leader = id;
    return id;
}
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

ulong DeterministicHash(int threads)
{
    var tickSystem = host.Simulation!.Systems.Single().Create(new SystemBuildContext(data, []))!;
    using var w = new World(new WorldSettings { Seed = 42, Threads = threads, MinPartitionSize = 1, PublishSnapshots = false },
        new OpenGroundWalkability((_, _) => 0), [tickSystem]);
    var id = Spawn(w, "Deterministic", true);
    w.TellReady(false);
    string token = (string)tickSystem.GetType().GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tickSystem)!;
    var edit = (SimCommand)Activator.CreateInstance(commandType, token, id, 5)!;
    w.Commands.Enqueue(edit with { Tick = 0 });
    w.RunTicks(3);
    return w.StateHash();
}
