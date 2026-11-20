using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

namespace CharTweak3;

internal sealed record ColourTarget(string Session, CharacterId Character, ColourMode Mode, ColourPalette Palette, ColourPalette Uniform)
{
    internal static ColourTarget? Read(UiFrameContext frame)
    {
        var parts = frame.Text("chartweak3.colours").Split('|');
        if (parts.Length != 8) return null;
        var numbers = new int[7];
        for (int i = 0; i < numbers.Length; i++)
            if (!int.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out numbers[i])) return null;
        return new(parts[0], new(numbers[0], numbers[1]), (ColourMode)numbers[2],
            new(numbers[3], numbers[4]), new(numbers[5], numbers[6]));
    }
}

/// <summary>Routes a private native modal screen through Meitou's text, slider, focus and pointer handling.</summary>
internal static class ColourEditor
{
    private static readonly ConditionalWeakTable<MyGuiScreenHost, Panel> Panels = new();
    private static FieldInfo s_screens = null!, s_routerPlayer = null!, s_session = null!;
    private static MethodInfo s_pause = null!;
    private static ModContext s_context = null!;
    private static PaletteLibrary s_library = null!;

    internal static void Install(ModContext context)
    {
        s_context = context;
        s_library = new(PaletteLibrary.DefaultPath(), context.LogError);
        var player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
        var router = typeof(IMod).Assembly.GetType("Meitou.Game.Ui.ScreenRouter", true)!;
        s_routerPlayer = router.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == player);
        s_screens = AccessTools.DeclaredField(player, "FaithfulScreens");
        s_session = AccessTools.DeclaredField(player, "Session");
        s_pause = AccessTools.DeclaredMethod(player, "UpdateMenuPause", Type.EmptyTypes);
        if (s_screens?.FieldType != typeof(MyGuiScreenHost) || s_session?.FieldType != typeof(WorldSession) || s_pause is null)
            throw new NotSupportedException("Unsupported MeitouClient color screen host.");
        void Patch(string method, string prefix, params Type[] parameters)
        {
            var target = AccessTools.DeclaredMethod(router, method, parameters)
                ?? throw new MissingMethodException($"Unsupported MeitouClient screen hook: {method}.");
            context.Harmony.Patch(target, prefix: new HarmonyMethod(typeof(ColourEditor), prefix));
        }
        Patch("BindFaithfulScreen", nameof(Bind), typeof(string));
        Patch("AddFaithfulContent", nameof(Decorate), typeof(string), typeof(List<UiControl>));
        Patch("FaithfulAction", nameof(Action), typeof(MyGuiEvent));
    }

    internal static bool Open(UiFrameContext frame, out string status)
    {
        status = "Select a living player character with an appearance.";
        var target = ColourTarget.Read(frame);
        if (target is null || !PersonalityUiBridge.TryGetEditorTarget(frame, out var player, out var id) || id != target.Character) return false;
        var screens = (MyGuiScreenHost)s_screens.GetValue(player)!;
        if (screens.HasModal) { status = "Close the other window first."; return false; }
        var panel = Panels.GetValue(screens, _ => new Panel(player!, screens, s_library, s_context));
        panel.Open(frame, target);
        status = "";
        return true;
    }

    private static Panel? Find(object router) => s_screens.GetValue(s_routerPlayer.GetValue(router)) is MyGuiScreenHost screens
        && Panels.TryGetValue(screens, out var panel) ? panel : null;
    private static bool Bind(object __instance, string id, ref UiBindings __result)
    {
        if (id == MainPanelDrag.Screen)
        {
            __result = MainPanelDrag.Bind((MyGuiScreenHost)s_screens.GetValue(s_routerPlayer.GetValue(__instance))!);
            return false;
        }
        if (id != ColourLayout.Screen) return true;
        __result = Find(__instance)?.Bind() ?? new UiBindings();
        return false;
    }
    private static bool Decorate(object __instance, string id, List<UiControl> controls)
    {
        if (id == MainPanelDrag.Screen) return false;
        if (id != ColourLayout.Screen) return true;
        Find(__instance)?.Decorate(controls);
        return false;
    }
    private static bool Action(object __instance, MyGuiEvent e)
    {
        if (e.Screen != ColourLayout.Screen) return true;
        Find(__instance)?.Action(e);
        return false;
    }

    private sealed class Panel(object player, MyGuiScreenHost screens, PaletteLibrary library, ModContext context)
    {
        private static readonly (string Name, int RGB)[] Quick =
        [ ("Black", 0x191919), ("White", 0xDCDCDC), ("Grey", 0x696969), ("Dark Red", 0x912823), ("Orange", 0xB95A23),
          ("Ochre", 0xAF8C2D), ("Green", 0x2D6937), ("Blue", 0x2D4682), ("Purple", 0x64377D), ("Brown", 0x5F3C23),
          ("Bone", 0xB9AA87) ];
        private UiFrameContext _frame = null!;
        private ColourTarget _target = null!;
        private ColourMode _mode;
        private ColourPalette _palette = ColourPalette.Default, _personal = ColourPalette.Default, _uniform = ColourPalette.Default;
        private string _nonce = "", _status = "";
        private int _preset = -1;
        private bool _deleteAsked;
        private ColourAssignment? _pending;
        private ColourPalette? _pendingDraft;
        private ColourMode _pendingMode;

        internal void Open(UiFrameContext frame, ColourTarget target)
        {
            _frame = frame; _target = target; _nonce = Guid.NewGuid().ToString("N");
            _mode = target.Mode == ColourMode.Uniform ? ColourMode.Uniform : ColourMode.Personal;
            _personal = target.Palette; _uniform = target.Uniform;
            _palette = _mode == ColourMode.Personal ? _personal : _uniform;
            _status = library.Error ?? "Choose colors, then Apply.";
            _preset = library.Presets.Count == 0 ? -1 : 0;
            _deleteAsked = false;
            _pending = null;
            if (!screens.IsRegistered(ColourLayout.Screen)) screens.Register(ColourLayout.Screen, ColourLayout.Create(), modal: true, centreRoot: true);
            screens.SetText(ColourLayout.Screen, "PresetName", "");
            SyncFields();
            screens.Open(ColourLayout.Screen);
            s_pause.Invoke(player, null);
        }

        private void SyncFields()
        {
            screens.SetText(ColourLayout.Screen, "PrimaryHex", ColourPalette.Hex(_palette.Primary));
            screens.SetText(ColourLayout.Screen, "SecondaryHex", ColourPalette.Hex(_palette.Secondary));
            SyncSliders();
        }
        private void SyncSliders()
        {
            foreach (string key in new[] { "Primary", "Secondary" })
            {
                int rgb = key == "Primary" ? _palette.Primary : _palette.Secondary;
                for (int channel = 0; channel < 3; channel++) screens.SetScroll(ColourLayout.Screen, key + "RGB"[channel], (rgb >> ((2 - channel) * 8)) & 255);
            }
        }
        private bool ValidFields() => ColourPalette.TryHex(screens.GetText(ColourLayout.Screen, "PrimaryHex"), out _)
            && ColourPalette.TryHex(screens.GetText(ColourLayout.Screen, "SecondaryHex"), out _);

        internal UiBindings Bind()
        {
            Acknowledge();
            var b = new UiBindings();
            b.Text["Character"] = PersonalityUiBridge.Name(_frame);
            b.Text["ModeHint"] = _mode == ColourMode.Personal ? "Personal palette — this character only" : "Shared uniform — Apply updates every enrolled character";
            b.Text["Personal"] = (_mode == ColourMode.Personal ? "[Selected] " : "") + "Personal";
            b.Text["Uniform"] = (_mode == ColourMode.Uniform ? "[Selected] " : "") + "Uniform";
            b.Text["Apply"] = _mode == ColourMode.Uniform ? "Apply uniform" : "Apply personal";
            b.Text["Status"] = _status;
            foreach (string key in new[] { "Primary", "Secondary" })
            {
                int rgb = key == "Primary" ? _palette.Primary : _palette.Secondary;
                b.Text[key + "Quick"] = Quick.FirstOrDefault(q => q.RGB == rgb).Name ?? "Custom";
                for (int channel = 0; channel < 3; channel++) b.Text[key + "RGB"[channel] + "Value"] = ((rgb >> ((2 - channel) * 8)) & 255).ToString(CultureInfo.InvariantCulture);
            }
            b.Text["Preset"] = Selected()?.Name ?? "No saved palettes";
            foreach (var widget in screens.Layout(ColourLayout.Screen).Widgets.Where(w => w.Type == "Button" && !w.Name.Contains('@')))
                b.Actions[widget.Name] = _nonce;
            if (!ValidFields()) b.Disabled.Add("Apply");
            if (Selected() is null)
                foreach (string name in new[] { "LoadPreset", "OverwritePreset", "DeletePreset", "PresetPrevious", "PresetNext" }) b.Disabled.Add(name);
            if (library.Error is not null)
                foreach (string name in new[] { "SavePreset", "OverwritePreset", "DeletePreset" }) b.Disabled.Add(name);
            return b;
        }

        private void Acknowledge()
        {
            if (_pending is not { } pending) return;
            var session = (WorldSession)s_session.GetValue(player)!;
            var system = session.World.Systems.OfType<ColourSystem>().FirstOrDefault();
            if (system?.Session != _target.Session || !system.View.TryGetValue(_target.Character, out var view)
                || view.Mode != pending.Mode || pending.Mode != ColourMode.Original && view.Palette != pending.Palette) return;
            bool sameDraft = _palette == _pendingDraft && _mode == _pendingMode;
            _uniform = view.Uniform;
            if (sameDraft && pending.Mode == ColourMode.Original)
            {
                _mode = ColourMode.Personal; _palette = _personal = view.Palette;
                SyncFields();
            }
            _status = sameDraft ? pending.Mode == ColourMode.Original ? "Original colors restored." : "Colors applied."
                : "Previous colors applied; draft has new changes.";
            _pending = null;
        }

        internal void Decorate(List<UiControl> controls)
        {
            for (int i = 0; i < controls.Count; i++)
            {
                var c = controls[i];
                var background = c.Type switch
                {
                    "Button" => new Vector4(0.24f, 0.22f, 0.18f, 1),
                    "EditBox" or "ScrollBar" => new Vector4(0.16f, 0.16f, 0.14f, 1),
                    _ => Vector4.Zero,
                };
                if (c.Name == "ColourWindow") background = new(0.10f, 0.10f, 0.09f, 1);
                if (c.Name.EndsWith("@Track", StringComparison.Ordinal)) background = new(0.65f, 0.56f, 0.39f, 1);
                bool swatch = c.Name is "PrimarySwatch" or "SecondarySwatch";
                if (swatch)
                {
                    int rgb = c.Name == "PrimarySwatch" ? _palette.Primary : _palette.Secondary;
                    background = new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
                }
                controls[i] = c with { Background = background, HoverColour = swatch ? background : new Vector4(0.36f, 0.33f, 0.27f, 1),
                    ActiveColour = swatch ? background : c.ActiveColour,
                    TextColour = new Vector4(0.92f, 0.92f, 0.88f, 1), DisabledColour = new Vector4(0.13f, 0.13f, 0.12f, 1),
                    Pickable = c.Name == "ColourWindow" ? true : c.Pickable };
            }
        }

        private NamedPalette? Selected() => _preset >= 0 && _preset < library.Presets.Count ? library.Presets[_preset] : null;
        private void Close() { screens.Close(ColourLayout.Screen); s_pause.Invoke(player, null); }

        internal void Action(MyGuiEvent e)
        {
            if (!screens.IsOpen(ColourLayout.Screen)) return;
            if (e.Widget != "DeletePreset") _deleteAsked = false;
            if (e.Action == "text")
            {
                if (e.Widget is "PrimaryHex" or "SecondaryHex")
                {
                    if (ColourPalette.TryHex(e.Text, out int rgb))
                    {
                        _palette = e.Widget == "PrimaryHex" ? _palette with { Primary = rgb } : _palette with { Secondary = rgb };
                        SyncSliders(); _status = "Preview changed. Press Apply to use it.";
                    }
                    else _status = "Enter six hex digits, for example B42D23 or #B42D23.";
                }
                return;
            }
            if (e.Action == "scroll" && e.Widget.Length > 1)
            {
                bool primary = e.Widget.StartsWith("Primary", StringComparison.Ordinal);
                string key = primary ? "Primary" : "Secondary";
                int channel = "RGB".IndexOf(e.Widget[^1]);
                if (channel < 0 || e.Widget != key + "RGB"[channel] || !float.IsFinite(e.Value)) return;
                int shift = (2 - channel) * 8, value = (int)Math.Clamp(MathF.Round(e.Value), 0, 255);
                int rgb = ((primary ? _palette.Primary : _palette.Secondary) & ~(255 << shift)) | (value << shift);
                _palette = primary ? _palette with { Primary = rgb } : _palette with { Secondary = rgb };
                SyncFields(); _status = "Preview changed. Press Apply to use it.";
                return;
            }
            if (e.Action != _nonce) return;
            switch (e.Widget)
            {
                case "Personal": case "Uniform":
                    if (_mode == ColourMode.Personal) _personal = _palette; else _uniform = _palette;
                    _mode = e.Widget == "Personal" ? ColourMode.Personal : ColourMode.Uniform;
                    _palette = _mode == ColourMode.Personal ? _personal : _uniform;
                    SyncFields(); _status = "Choose colors, then Apply.";
                    break;
                case "PrimarySwatch": case "SecondarySwatch":
                case "PrimaryPrevious": case "PrimaryNext": case "SecondaryPrevious": case "SecondaryNext":
                    bool primary = e.Widget.StartsWith("Primary", StringComparison.Ordinal);
                    int current = Array.FindIndex(Quick, q => q.RGB == (primary ? _palette.Primary : _palette.Secondary));
                    int step = e.Widget.EndsWith("Previous", StringComparison.Ordinal) ? -1 : 1;
                    int index = current < 0 ? (step > 0 ? 0 : Quick.Length - 1) : (current + step + Quick.Length) % Quick.Length;
                    _palette = primary ? _palette with { Primary = Quick[index].RGB } : _palette with { Secondary = Quick[index].RGB };
                    SyncFields(); _status = "Preview changed. Press Apply to use it."; break;
                case "PresetPrevious": case "PresetNext":
                    if (library.Presets.Count > 0)
                    {
                        _preset = (_preset + (e.Widget == "PresetPrevious" ? -1 : 1) + library.Presets.Count) % library.Presets.Count;
                        screens.SetText(ColourLayout.Screen, "PresetName", Selected()!.Name);
                    }
                    break;
                case "LoadPreset":
                    if (Selected() is { } selected)
                    {
                        _palette = selected.Colours; SyncFields();
                        screens.SetText(ColourLayout.Screen, "PresetName", selected.Name);
                        _status = "Preset loaded. Press Apply to use it.";
                    }
                    break;
                case "SavePreset": case "OverwritePreset":
                    if (!ValidFields()) { _status = "Fix the hex colors before saving."; break; }
                    string name = e.Widget == "OverwritePreset" ? Selected()?.Name ?? "" : screens.GetText(ColourLayout.Screen, "PresetName");
                    if (library.Save(name, _palette, e.Widget == "OverwritePreset", out _status))
                    {
                        _preset = library.Presets.ToList().FindIndex(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                        screens.SetText(ColourLayout.Screen, "PresetName", Selected()!.Name);
                    }
                    break;
                case "DeletePreset":
                    if (Selected() is not { } deleting) break;
                    if (!_deleteAsked) { _deleteAsked = true; _status = "Press Delete selected again to confirm."; break; }
                    library.Delete(deleting.Name, out _status); _deleteAsked = false;
                    _preset = Math.Min(_preset, library.Presets.Count - 1);
                    break;
                case "Apply": case "Reset":
                    if (e.Widget == "Apply" && !ValidFields()) { _status = "Fix the hex colors before applying."; break; }
                    if (!PersonalityUiBridge.TryGetEditorTarget(_frame, out _, out var id) || id != _target.Character)
                    { _status = "Selection changed. Close and reopen colors."; break; }
                    context.Enqueue(new SetColours(_target.Session, id, e.Widget == "Reset" ? ColourMode.Original : _mode, _palette) { Tick = context.Tick });
                    _pending = new(e.Widget == "Reset" ? ColourMode.Original : _mode, _palette);
                    _pendingDraft = _palette; _pendingMode = _mode;
                    _status = "Colors queued.";
                    break;
                case "Cancel": Close(); break;
            }
        }
    }
}
