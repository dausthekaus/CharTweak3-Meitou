using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Textures;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Characters;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Player;

internal static class PhotoChecks
{
    internal static void Run(ModHost host, WorldSession session, object player, CharacterId first, CharacterId second, CharacterId npc,
        string source, string invalid, object files)
    {
        var mod = host.Mods.Single().Assemblies.Single();
        Type Type(string name) => mod.GetType("CharTweak3." + name, true)!;
        object? Call(string type, string method, object? instance, params object?[] args) => AccessTools.Method(Type(type), method).Invoke(instance, args);
        Check((bool)AccessTools.Property(Type("PhotoRendering"), "Available").GetValue(null)!, "photo rendering hooks validate against actual runtime");
        var camera = (Eye: new Vector3(0, 12, 10), Pivot: new Vector3(0, 12, 0));
        var focused = ((Vector3 Eye, Vector3 Pivot))Call("PhotoRendering", "FrameCamera", null, camera, new Vector3(2, 20, 1), true, new Vector2(1, 2))!;
        Check(focused.Pivot == new Vector3(3, 22, 1) && focused.Eye == new Vector3(3, 22, 11)
            && focused.Eye - focused.Pivot == camera.Eye - camera.Pivot, "head focus and panning translate the camera without changing its orbit direction");
        var fallback = ((Vector3 Eye, Vector3 Pivot))Call("PhotoRendering", "FrameCamera", null, camera, null, true, Vector2.Zero)!;
        Check(fallback == camera, "a missing live head retains the native head-camera fallback");
        var turned = ((Vector3 Eye, Vector3 Pivot))Call("PhotoRendering", "FrameCamera", null,
            (new Vector3(10, 12, 0), new Vector3(0, 12, 0)), new Vector3(2, 20, 1), false, Vector2.UnitX)!;
        Check(turned.Pivot == new Vector3(0, 12, -1), "panning uses the screen's right direction after camera rotation");
        object?[] untouched = [new object(), default(EditorStageRequest), camera];
        AccessTools.Method(Type("PhotoRendering"), "Camera").Invoke(null, untouched);
        Check(((Vector3, Vector3))untouched[2]! == camera, "photo camera hook leaves unrelated native stages unchanged");
        var foreground = new RgbaImage(2, 2, [255, 0, 0, 255, 0, 0, 0, 0, 0, 0, 255, 128, 255, 255, 255, 255]);
        var background = new RgbaImage(4, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 255, 0, 0, 255,
            255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 255, 0, 0, 255]);
        var composed = (RgbaImage)Call("PhotoImage", "Compose", null, foreground, background)!;
        Check(composed[0, 0] == (255, 0, 0, 255) && composed[1, 0] == (0, 255, 0, 255)
            && composed[0, 1] == (0, 127, 128, 255), "photo composites RGBA over a centered background crop");
        var plain = (RgbaImage)Call("PhotoImage", "Compose", null, foreground, null)!;
        Check(plain[1, 0] == (48, 48, 48, 255), "photo without an image uses the preview's plain background");
        var readback = (RgbaImage)Call("PhotoImage", "FromReadback", null,
            new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 255, 0, 0, 255, 255, 0, 0, 255 }, 2)!;
        Check(readback[0, 0].R == 255 && readback[0, 1].B == 255, "photo flips GPU bottom-up rows exactly once");
        try { Call("PhotoImage", "FromReadback", null, new byte[16], 2); throw new Exception("empty readback accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { }
        var stored = Call("PortraitFiles", "FromPixels", files, composed)!;
        string path = (string)AccessTools.Property(stored.GetType(), "Path").GetValue(stored)!;
        Check(TextureLoader.LoadImage(File.ReadAllBytes(path))[0, 1] == composed[0, 1], "photo uses existing normalized PNG cache without changing its pixels");

        var type = player.GetType();
        var screens = (MyGuiScreenHost)AccessTools.Field(type, "FaithfulScreens").GetValue(player)!;
        var router = AccessTools.Property(type, "ScreenRouter").GetValue(player)!;
        screens.Activated += e => AccessTools.Method(router.GetType(), "FaithfulAction").Invoke(router, [e]);
        var ui = (UiHost)AccessTools.Field(type, "_uiHost").GetValue(player)!;
        UiBindings Bind() => (UiBindings)AccessTools.Method(type, "BindUi").Invoke(player, null)!;
        IReadOnlyList<UiControl> Frame(int width = 1280, int height = 900) => screens.Arrange(width, height,
            id => (UiBindings)AccessTools.Method(router.GetType(), "BindFaithfulScreen").Invoke(router, [id])!,
            (id, controls) => AccessTools.Method(router.GetType(), "AddFaithfulContent").Invoke(router, [id, controls])).Single(f => f.Id == "chartweak3-photo").Controls;
        UiControl Control(string name) => Frame().Single(c => c.Name == name);
        void Click(string name)
        {
            var c = Control(name); screens.PointerDown(new(c.Bounds.X + c.Bounds.Width / 2, c.Bounds.Y + c.Bounds.Height / 2)); screens.PointerUp();
        }
        void Select(CharacterId id) { session.World.Commands.Enqueue(new SelectCommand([id]) { Tick = session.World.Tick }); session.World.ApplyPausedInterfaceCommands(); }
        object Shot()
        {
            var table = AccessTools.Field(Type("PhotoRendering"), "Players").GetValue(null)!;
            object?[] args = [player, null]; AccessTools.Method(table.GetType(), "TryGetValue").Invoke(table, args); return args[1]!;
        }
        bool PictureFinished()
        {
            var table = AccessTools.Field(Type("PhotoEditor"), "Panels").GetValue(null)!;
            object?[] args = [screens, null]; AccessTools.Method(table.GetType(), "TryGetValue").Invoke(table, args);
            return AccessTools.Field(args[1]!.GetType(), "_picture").GetValue(args[1]) is null;
        }
        void Ready() => AccessTools.Field(Shot().GetType(), "Ready").SetValue(Shot(), true);
        float CameraValue(string name) => (float)AccessTools.Field(Shot().GetType(), name).GetValue(Shot())!;
        EditorStageRequest Request() => (EditorStageRequest)AccessTools.Method(Shot().GetType(), "CreateRequest").Invoke(Shot(), [42L])!;
        TaskCompletionSource<RgbaImage> Pending() => (TaskCompletionSource<RgbaImage>)AccessTools.Field(Shot().GetType(), "Capture").GetValue(Shot())!;
        void Wait(Func<bool> done)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(5)) { Frame(); if (done()) return; Thread.Sleep(5); }
            throw new Exception("Photo did not complete: " + Control("Status").Caption);
        }
        foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
        {
            AccessTools.Property(type, "UiMode").SetValue(player, mode);
            var registrations = host.RegisterUi(extension => ui.Register(extension));
            IReadOnlyList<UiControl> Menu() => ui.Arrange(mode, 1280, 900, Bind(), new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, 1280, 900) });
            void MenuClick(string name) => ui.Dispatch(Menu().Single(c => c.Name == "chartweak3-meitou:" + name));
            Select(first); MenuClick("open"); Check(Menu().Single(c => c.Name == "chartweak3-meitou:photo").Enabled, "living player can open Photo");
            MenuClick("photo");
            Check(screens.HasModal && session.Simulation.RequestedPause && Control("Character").Caption == "Portrait Pin"
                && !Control("Picture").Enabled, "photo modal pauses and waits for a valid rendered preview");
            screens.SetOffset("chartweak3-photo", Vector2.Zero);
            Check(Frame(800, 600).All(c => c.Bounds.X >= 0 && c.Bounds.Y >= 0 && c.Bounds.X + c.Bounds.Width <= 800 && c.Bounds.Y + c.Bounds.Height <= 600),
                "photo window fits 800x600");
            var before = Control("PhotoWindow").Bounds; var title = new Vector2(before.X + before.Width / 2, before.Y + 16);
            Check(screens.PointerDown(title) && screens.PointerMove(title + new Vector2(-80, 20)), "photo window uses native dragging"); screens.PointerUp();
            Check(Control("PhotoWindow").Bounds.X == before.X - 80, "photo drag moves the whole window");
            ulong hash = session.World.StateHash(); Click("TurnLeft"); Click("Closer");
            Check(session.World.StateHash() == hash && (float)AccessTools.Field(Shot().GetType(), "Distance").GetValue(Shot())! == 38,
                "photo framing changes only preview state");
            float yaw = CameraValue("Yaw");
            Click("FocusHead"); Click("AngleUp"); Click("PanUp"); Click("PanRight"); Click("Closer");
            Check(Request().Head && Request().Distance == 8.5f && Request().Elevation > .08f && Request().Yaw == yaw
                && (Vector2)AccessTools.Field(Shot().GetType(), "Pan").GetValue(Shot())! == new Vector2(.25f, .25f)
                && Control("FocusHead").Caption.Contains("[x]"), "head preset, fine zoom, tilt and panning reach the native photo request");
            for (int i = 0; i < 30; i++) { Click("Closer"); Click("AngleUp"); }
            Check(Request().Distance == 2 && Request().Elevation == 1.3f, "head zoom and vertical orbit clamp before clipping or flipping");
            for (int i = 0; i < 40; i++) Click("AngleDown");
            Check(Request().Elevation == -1.3f, "downward orbit is bounded");
            Click("FocusBody");
            Check(!Request().Head && Request().Distance == 42 && Request().Yaw == yaw
                && (Vector2)AccessTools.Field(Shot().GetType(), "Pan").GetValue(Shot())! == Vector2.Zero, "full-body preset recenters without losing the chosen yaw");
            Click("ResetCamera");
            Check(Request().Yaw == .25f && Request().Elevation == .08f && Request().Distance == 42 && !Request().Head
                && session.World.StateHash() == hash, "Reset view restores camera defaults without changing the character");
            Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", source); Click("ChooseBackground");
            Wait(() => Control("Status").Caption.StartsWith("Background ready"));
            string backgroundPath = Control("Background").Properties["ImageTexture"];
            Check(backgroundPath.StartsWith("file:") && session.World.StateHash() == hash, "background chooser prepares preview without assigning a portrait");
            Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", invalid); Click("ChooseBackground");
            Wait(() => Control("ChooseBackground").Enabled);
            Check(Control("Background").Properties["ImageTexture"] == backgroundPath, "invalid background preserves the previous one");
            Click("FocusHead"); Click("ResetCamera");
            Check(Control("Background").Properties["ImageTexture"] == backgroundPath, "camera reset retains the chosen background");
            Ready(); Click("Picture");
            var capture = Pending(); Check(capture is not null && !Control("Picture").Enabled, "Picture waits for render-thread capture");
            capture!.SetResult(foreground);
            Wait(() => Control("Status").Caption == "Applying photo..."); session.ApplyPausedCommandsNow();
            Check(Control("Status").Caption.StartsWith("Photo applied") && screens.IsOpen("chartweak3-photo"), "completed photo applies through existing paused portrait command and keeps composer open");
            Click("Back"); Check(!screens.HasModal && Menu().Any(c => c.Name == "chartweak3-meitou:window"), "photo Back restores main menu");
            MenuClick("portrait-reset"); session.ApplyPausedCommandsNow(); Menu();
            MenuClick("photo"); Ready(); Click("Picture"); var stale = Pending();
            Select(second); stale.SetResult(foreground);
            Wait(PictureFinished);
            session.ApplyPausedCommandsNow();
            Check(Call("PortraitSave", "Get", null, session.World.Characters.Cold(first.Slot)) is null
                && Call("PortraitSave", "Get", null, session.World.Characters.Cold(second.Slot)) is null, "stale photo never assigns to original or replacement selection");
            Click("Cancel");
            Select(first); MenuClick("open"); MenuClick("photo"); Ready(); Click("Picture"); var cancelled = Pending(); Click("Cancel");
            Check(cancelled.Task.IsCanceled && !screens.HasModal, "closing photo cancels a pending readback");
            session.World.Commands.Enqueue(new InspectCommand(npc) { Tick = session.World.Tick }); session.World.ApplyPausedInterfaceCommands();
            MenuClick("open"); Check(!Menu().Single(c => c.Name == "chartweak3-meitou:photo").Enabled, "NPC photo capture disabled");
            MenuClick("close"); foreach (var registration in registrations) registration.Dispose();
            Environment.SetEnvironmentVariable("CHARTWEAK_TEST_PICK", source);
            Console.WriteLine($"PASS: {mode} photo UI, native drag/pause, background selection, framing isolation, capture completion, stale/cancel guards and portrait assignment (synthetic readback).");
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
