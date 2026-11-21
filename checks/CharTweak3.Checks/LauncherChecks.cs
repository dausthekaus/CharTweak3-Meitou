using HarmonyLib;
using System.Text.Json;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

internal static class LauncherChecks
{
    internal static void Run(ModHost host, WorldSession session, object player)
    {
        var type = player.GetType();
        var ui = (UiHost)AccessTools.Field(type, "_uiHost").GetValue(player)!;
        var screens = (MyGuiScreenHost)AccessTools.Field(type, "FaithfulScreens").GetValue(player)!;
        var group = host.Settings.Single(g => g.ModId == "chartweak3-meitou");
        var visible = group.Settings.Single(s => s.Key == "ShowLauncher");
        var launcherType = host.Mods.Single().Assemblies.Single().GetType("CharTweak3.LauncherVisibility", true)!;
        var launcher = AccessTools.Field(launcherType, "s_compatibility").GetValue(null)!;
        string path = (string)AccessTools.Property(launcherType, "PreferencePath").GetValue(null)!;
        if (launcher is null) { RunNative(host, session, player, visible, path); return; }
        UiBindings Bind() => (UiBindings)AccessTools.Method(type, "BindUi").Invoke(player, null)!;
        void Press()
        {
            session.Input.SetKey(Key.Backslash, true); session.Tick();
            session.Input.SetKey(Key.Backslash, false); session.Tick();
        }
        try
        {
            foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
            {
                AccessTools.Property(type, "UiMode").SetValue(player, mode);
                var registrations = host.RegisterUi(extension => ui.Register(extension));
                IReadOnlyList<UiControl> Frame() => ui.Arrange(mode, 800, 600, Bind(), new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, 800, 600) });
                bool Has(string name) => Frame().Any(c => c.Name == "chartweak3-meitou:" + name);
                void Click(string name) => ui.Dispatch(Frame().Single(c => c.Name == "chartweak3-meitou:" + name));
                Check((bool)visible.Get()! && Has("open"), "launcher is visible by default");
                Click("open"); Click("launcher");
                Check(!Has("open") && Has("window"), "Hide button removes only the launcher and keeps the main window open");
                using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
                    Check(saved.RootElement.GetProperty("ShowLauncher").GetBoolean() == false, "native Changed event persists hidden state to portable preference");
                Check(AccessTools.Method(launcherType, "ReadPreference").Invoke(launcher, null) is false, "visibility survives reading from disk");
                visible.Reset();
                AccessTools.Method(group.GetType(), "Load").Invoke(group, [new Dictionary<string, string> { ["ShowLauncher"] = "false" }]);
                Check(!Has("open"), "saved native launcher preference restores");
                long tick = session.World.Tick;
                Press(); Check(Has("open") && Has("window") && session.World.Tick == tick, "backslash restores launcher without closing window or advancing gameplay");
                session.Input.SetKey(Key.Backslash, true); session.Tick();
                Check(!Has("open"), "press hides launcher");
                session.Tick(); Check(!Has("open"), "holding the key does not repeatedly toggle");
                session.Input.SetKey(Key.Backslash, false); session.Tick(); Press();
                Click("close"); Press();
                Check(!Has("open") && !Has("window") && MyGuiPresentation.HitTest(Frame(), 30, 110) is null, "hidden launcher leaves no hit region when the window is closed");
                Press(); Check(Has("open") && !Has("window"), "key restores closed launcher without opening window");
                var before = session.Bindings.Get(InputAction.ToggleInventory).ToArray();
                session.Bindings.Set(InputAction.ToggleInventory, Key.Backslash);
                Press(); Check(Has("open"), "compatibility key respects existing game bindings");
                session.Bindings.Set(InputAction.ToggleInventory, before);
                if (!screens.IsRegistered("title")) screens.Register("title", new MyGuiLayout("", []));
                screens.Open("title"); Press(); Check(Has("open"), "title screen ignores compatibility toggle"); screens.Close("title");
                Check(session.Bindings.Get(InputAction.ToggleStats).Contains(Binding.Of(Key.F11)), "F11 frame statistics binding is retained");
                foreach (var registration in registrations) registration.Dispose();
                Console.WriteLine($"PASS: {mode} launcher hide/show, preference persistence/recovery, key edges, no hidden hit region and binding/title guards.");
            }
        }
        finally { visible.Reset(); }
    }

    // Newer clients dispatch mod key actions in GameHost, outside this headless WorldSession.
    // Use their real bindings and press callback, with the same action edge the host dispatches.
    private static void RunNative(ModHost host, WorldSession session, object player, ModSetting visible, string path)
    {
        var actions = AccessTools.Property(typeof(ModHost), "KeyActions").GetValue(host)!;
        var action = ((System.Collections.IEnumerable)actions).Cast<object>().Single(a => (string)AccessTools.Property(a.GetType(), "Owner").GetValue(a)! == "chartweak3-meitou");
        var number = (InputAction)AccessTools.Property(action.GetType(), "Action").GetValue(action)!;
        AccessTools.Method(typeof(InputBindings), "UseModActions").Invoke(session.Bindings, [actions, null]);
        Check(session.Bindings.Get(number).Contains(Binding.Of(Key.Backslash)), "native Controls action defaults to backslash");
        var press = AccessTools.Method(action.GetType(), "Press");
        var type = player.GetType();
        var ui = (UiHost)AccessTools.Field(type, "_uiHost").GetValue(player)!;
        UiBindings Bind() => (UiBindings)AccessTools.Method(type, "BindUi").Invoke(player, null)!;
        void Tick()
        {
            var state = session.Tick();
            if (!state.Pressed(number)) return;
            var context = Activator.CreateInstance(press.GetParameters()[0].ParameterType, session.CurrentSnapshot,
                session.World.Player.Selection.ToArray(), session.World.Tick, (Action<SimCommand>)session.World.Commands.Enqueue);
            press.Invoke(action, [context]);
        }
        try
        {
            foreach (var mode in new[] { UiMode.Faithful, UiMode.Meitou })
            {
                AccessTools.Property(type, "UiMode").SetValue(player, mode);
                var registrations = host.RegisterUi(extension => ui.Register(extension));
                IReadOnlyList<UiControl> Frame() => ui.Arrange(mode, 800, 600, Bind(), new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, 800, 600) });
                bool Has(string name) => Frame().Any(c => c.Name == "chartweak3-meitou:" + name);
                void Click(string name) => ui.Dispatch(Frame().Single(c => c.Name == "chartweak3-meitou:" + name));
                visible.Reset(); Click("open"); Click("launcher");
                Check(!Has("open") && Has("window"), "native-action client can hide launcher while retaining main menu");
                using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
                    Check(!saved.RootElement.GetProperty("ShowLauncher").GetBoolean(), "native-action client persists launcher visibility");
                long tick = session.World.Tick;
                session.Input.SetKey(Key.Backslash, true); Tick();
                Check(Has("open") && Has("window") && session.World.Tick == tick, "native backslash action restores launcher while paused");
                Tick(); Check(Has("open"), "native held key does not toggle twice");
                session.Input.SetKey(Key.Backslash, false); Tick();
                Click("close"); Click("open"); Click("launcher"); Click("close");
                session.Input.SetKey(Key.Backslash, true); Tick();
                Check(Has("open") && !Has("window"), "native Controls action recovers hidden launcher with menu closed");
                session.Input.SetKey(Key.Backslash, false); Tick();
                Check(session.Bindings.Get(InputAction.ToggleStats).Contains(Binding.Of(Key.F11)), "native action retains F11 frame statistics");
                foreach (var registration in registrations) registration.Dispose();
                Console.WriteLine($"PASS: {mode} native Controls registration, real binding/press callback, held edges, visibility persistence and closed-menu recovery.");
            }
        }
        finally { visible.Reset(); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
