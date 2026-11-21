using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Textures;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>A native modal photo composer. Only a completed and revalidated Picture queues a portrait assignment.</summary>
internal static class PhotoEditor
{
    private static readonly ConditionalWeakTable<MyGuiScreenHost, Panel> Panels = new();
    private static FieldInfo s_screens = null!, s_player = null!, s_session = null!;
    private static MethodInfo s_pause = null!;
    private static ModContext s_context = null!;
    private static PortraitFiles s_files = null!;

    internal static void Install(ModContext context, PortraitFiles files)
    {
        s_context = context; s_files = files;
        var player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        var router = typeof(IMod).Assembly.GetType("Meitou.Game.Ui.ScreenRouter", true)!;
        s_screens = AccessTools.Field(player, "FaithfulScreens"); s_session = AccessTools.Field(player, "Session");
        s_player = router.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == player);
        s_pause = AccessTools.Method(player, "UpdateMenuPause", Type.EmptyTypes);
        foreach (var (method, handler) in new[] { ("BindFaithfulScreen", nameof(Bind)), ("AddFaithfulContent", nameof(Decorate)), ("FaithfulAction", nameof(Action)) })
            context.Harmony.Patch(AccessTools.DeclaredMethod(router, method), prefix: new HarmonyMethod(typeof(PhotoEditor), handler));
    }

    internal static bool CanOpen(UiFrameContext frame) => PhotoRendering.Available && !PortraitPicker.IsOpen && PersonalityUiBridge.CanOpenEditor(frame);
    internal static bool Open(UiFrameContext frame, Action back, out string status)
    {
        status = "Select a living player character with an appearance.";
        if (!CanOpen(frame) || !PersonalityUiBridge.TryGetEditorTarget(frame, out var player, out var id)) return false;
        var screens = (MyGuiScreenHost)s_screens.GetValue(player)!;
        if (screens.HasModal) { status = "Close the other window first."; return false; }
        var system = ((WorldSession)s_session.GetValue(player)!).World.Systems.OfType<PortraitSystem>().FirstOrDefault();
        if (system is null) return false;
        Panels.GetValue(screens, _ => new Panel(player!, screens)).Open(frame, id, system.Session, back);
        status = ""; return true;
    }
    private static Panel? Find(object router) => s_screens.GetValue(s_player.GetValue(router)) is MyGuiScreenHost screens && Panels.TryGetValue(screens, out var p) ? p : null;
    private static bool Bind(object __instance, string id, ref UiBindings __result)
    {
        if (id != PhotoLayout.Screen) return true;
        __result = Find(__instance)?.Bind() ?? new UiBindings(); return false;
    }
    private static bool Decorate(object __instance, string id, List<UiControl> controls)
    {
        if (id != PhotoLayout.Screen) return true;
        Find(__instance)?.Decorate(controls); return false;
    }
    private static bool Action(object __instance, MyGuiEvent e)
    {
        if (e.Screen != PhotoLayout.Screen) return true;
        Find(__instance)?.Action(e); return false;
    }

    private sealed class Panel(object player, MyGuiScreenHost screens)
    {
        private UiFrameContext _frame = null!;
        private PhotoRendering.Shot _shot = null!;
        private string _session = "", _nonce = "", _status = "", _pending = "";
        private Action _back = null!;
        private PortraitAsset? _background;
        private Task<PortraitAsset?>? _choosing;
        private Task<PortraitAsset>? _picture;
        private bool Busy => _choosing is not null || _picture is not null || _pending.Length > 0;
        private PortraitSystem? System => ((WorldSession)s_session.GetValue(player)!).World.Systems.OfType<PortraitSystem>().FirstOrDefault();
        private bool Valid => System?.Session == _session && PersonalityUiBridge.TryGetEditorTarget(_frame, out _, out var id) && id == _shot.Character;

        internal void Open(UiFrameContext frame, Meitou.Simulation.CharacterId id, string session, Action back)
        {
            _frame = frame; _session = session; _back = back; _nonce = Guid.NewGuid().ToString("N");
            _background = null; _choosing = null; _picture = null; _pending = "";
            _status = "Choose a background and frame the character, then Picture.";
            _shot = PhotoRendering.Open(player, id);
            if (!screens.IsRegistered(PhotoLayout.Screen)) screens.Register(PhotoLayout.Screen, PhotoLayout.Create(), modal: true, centreRoot: true);
            screens.Open(PhotoLayout.Screen); s_pause.Invoke(player, null);
        }

        internal UiBindings Bind()
        {
            Update();
            var b = new UiBindings(); b.Text["Character"] = PersonalityUiBridge.Name(_frame);
            b.Text["FocusHead"] = _shot.Head ? "[x] Head" : "Head";
            b.Text["FocusBody"] = _shot.Head ? "Full body" : "[x] Body";
            b.Text["Status"] = !Valid ? "Selection changed. Go Back and reopen Photo." : _shot.Error ?? _status;
            foreach (var w in screens.Layout(PhotoLayout.Screen).Widgets.Where(w => w.Type == "Button"))
            {
                b.Actions[w.Name] = _nonce;
                if (w.Name is not ("Back" or "Cancel") && (Busy || !Valid || PortraitPicker.IsOpen)) b.Disabled.Add(w.Name);
            }
            if (!_shot.Ready || _shot.Error is not null) b.Disabled.Add("Picture");
            if (!_shot.Ready || !Valid) b.Hidden.Add("Preview");
            if (_background is { } image)
            {
                int side = Math.Min(image.Width, image.Height);
                b.Properties["Background"] = new Dictionary<string, string> { ["ImageTexture"] = "file:" + image.Path,
                    ["ImageRect"] = $"{(image.Width - side) / 2} {(image.Height - side) / 2} {side} {side}" };
            }
            return b;
        }

        private void Update()
        {
            if (_choosing is { IsCompleted: true } choosing)
            {
                _choosing = null;
                try { var image = choosing.GetAwaiter().GetResult(); if (Valid) { if (image is not null) _background = image; _status = image is null ? "Background selection cancelled." : "Background ready. Picture saves the composition."; } }
                catch (Exception e) { Report(e); }
            }
            if (_picture is { IsCompleted: true } picture)
            {
                _picture = null;
                try
                {
                    var image = picture.GetAwaiter().GetResult();
                    if (!Valid) { _status = "Selection changed; photo was not applied."; return; }
                    var system = System!;
                    if (system.View.Values.Select(v => v.Id).Distinct().Count() >= PortraitSystem.MaximumAssets && !system.View.Values.Any(v => v.Id == image.Id))
                    { _status = "Portrait limit reached (128 different images)."; return; }
                    if (!File.Exists(image.Path)) throw new IOException("Cannot write portrait storage. See log.");
                    s_context.Enqueue(new SetPortrait(_session, _shot.Character, image) { Tick = s_context.Tick });
                    _pending = image.Id; _status = "Applying photo...";
                }
                catch (Exception e) { Report(e); }
            }
            if (_pending.Length > 0 && Valid && System!.View.GetValueOrDefault(_shot.Character)?.Id == _pending)
            { _pending = ""; _status = "Photo applied. Save normally to keep it."; }
        }

        internal void Decorate(List<UiControl> controls)
        {
            for (int i = 0; i < controls.Count; i++)
            {
                var c = controls[i];
                bool selected = c.Name == "FocusHead" && _shot.Head || c.Name == "FocusBody" && !_shot.Head;
                controls[i] = c with { Background = c.Name == "PhotoWindow" ? new(.10f, .10f, .09f, 1)
                    : c.Name == "Background" ? new(48 / 255f, 48 / 255f, 48 / 255f, 1)
                    : selected ? new(.39f, .34f, .22f, 1)
                    : c.Type == "Button" ? new(.24f, .22f, .18f, 1) : Vector4.Zero,
                    HoverColour = new(.36f, .33f, .27f, 1), TextColour = new(.92f, .92f, .88f, 1),
                    DisabledColour = new(.13f, .13f, .12f, 1), Pickable = c.Name == "PhotoWindow" ? true : c.Pickable };
            }
        }

        private async Task<PortraitAsset?> ChooseBackground()
        {
            string? path = await PortraitPicker.ChooseBackground().ConfigureAwait(false);
            return path is null ? null : await Task.Run(() => s_files.Import(path)).ConfigureAwait(false);
        }
        private async Task<PortraitAsset> TakePicture()
        {
            var background = _background;
            var image = await PhotoRendering.Capture(_shot).ConfigureAwait(false);
            return await Task.Run(() => s_files.FromPixels(PhotoImage.Compose(image,
                background is null ? null : TextureLoader.LoadImage(background.Png)))).ConfigureAwait(false);
        }
        private void Report(Exception error) { _status = error.GetBaseException().Message; s_context.LogError("Photo: " + error); }
        private void Close()
        {
            PhotoRendering.Close(_shot); _choosing = null; _picture = null; _pending = "";
            screens.Close(PhotoLayout.Screen); s_pause.Invoke(player, null);
        }
        internal void Action(MyGuiEvent e)
        {
            if (!screens.IsOpen(PhotoLayout.Screen) || e.Action != _nonce) return;
            if (e.Widget == "Cancel") { Close(); return; }
            if (e.Widget == "Back") { Close(); _back(); return; }
            if (!Valid || Busy || PortraitPicker.IsOpen) return;
            switch (e.Widget)
            {
                case "ChooseBackground": _status = "Choose a background in the OS file chooser..."; _choosing = ChooseBackground(); break;
                case "ClearBackground": _background = null; _status = "Plain background selected."; break;
                case "TurnLeft": _shot.Yaw -= MathF.PI / 12; break;
                case "TurnRight": _shot.Yaw += MathF.PI / 12; break;
                case "AngleUp": _shot.Elevation = Math.Min(1.3f, _shot.Elevation + MathF.PI / 36); break;
                case "AngleDown": _shot.Elevation = Math.Max(-1.3f, _shot.Elevation - MathF.PI / 36); break;
                case "PanLeft": _shot.Pan.X = Math.Max(-64, _shot.Pan.X - (_shot.Head ? .25f : 1)); break;
                case "PanRight": _shot.Pan.X = Math.Min(64, _shot.Pan.X + (_shot.Head ? .25f : 1)); break;
                case "PanUp": _shot.Pan.Y = Math.Min(64, _shot.Pan.Y + (_shot.Head ? .25f : 1)); break;
                case "PanDown": _shot.Pan.Y = Math.Max(-64, _shot.Pan.Y - (_shot.Head ? .25f : 1)); break;
                case "FocusHead": case "FocusBody":
                    _shot.Head = e.Widget == "FocusHead"; _shot.Distance = _shot.Head ? 9 : 42;
                    _shot.Elevation = .08f; _shot.Pan = Vector2.Zero;
                    _status = _shot.Head ? "Head focus. Use Move frame for fine adjustments." : "Full body focus.";
                    break;
                case "ResetCamera": _shot.ResetCamera(); _status = "Camera reset to full body."; break;
                case "Closer": _shot.Distance = Math.Max(_shot.Head ? 2 : 12, _shot.Distance - (_shot.Head ? .5f : 4)); break;
                case "Further": _shot.Distance = Math.Min(100, _shot.Distance + (_shot.Head ? .5f : 4)); break;
                case "Picture": _status = "Taking picture..."; _picture = TakePicture(); break;
            }
        }
    }
}
