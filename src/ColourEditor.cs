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

    internal static bool Open(UiFrameContext frame, Action back, out string status)
    {
        status = "Select a living player character with an appearance.";
        var target = ColourTarget.Read(frame);
        if (target is null || !PersonalityUiBridge.TryGetEditorTarget(frame, out var player, out var id) || id != target.Character) return false;
        var screens = (MyGuiScreenHost)s_screens.GetValue(player)!;
        if (screens.HasModal) { status = "Close the other window first."; return false; }
        var panel = Panels.GetValue(screens, _ => new Panel(player!, screens, s_library, s_context));
        panel.Open(frame, target, back);
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
        private bool _advanced;
        private bool _includeSelected;
        private Vector2? _windowOrigin;
        private ColourPalette? _profilePalette;
        private Action _back = null!;

        internal void Open(UiFrameContext frame, ColourTarget target, Action back)
        {
            _frame = frame; _target = target; _nonce = Guid.NewGuid().ToString("N");
            _back = back;
            _mode = target.Mode == ColourMode.Uniform ? ColourMode.Uniform : ColourMode.Personal;
            _personal = target.Palette; _uniform = target.Uniform;
            _palette = _mode == ColourMode.Personal ? _personal : _uniform;
            _status = library.Error ?? "Choose colors, then Apply.";
            _preset = library.Presets.Count == 0 ? -1 : 0;
            _deleteAsked = false;
            _pending = null;
            _advanced = false;
            _includeSelected = false;
            if (!screens.IsRegistered(ColourLayout.Screen)) screens.Register(ColourLayout.Screen, ColourLayout.Create(), modal: true, centreRoot: true);
            else screens.Replace(ColourLayout.Screen, ColourLayout.Create());
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
            var b = new UiBindings { WindowPosition = _windowOrigin };
            BindProfile(b);
            b.Text["ModeHint"] = _mode == ColourMode.Personal ? "Personal colors affect this character only." : "Apply joins this character to the shared uniform.";
            if (_mode == ColourMode.Uniform && _includeSelected) b.Text["ModeHint"] = "Apply also joins the selected squad members to the uniform.";
            b.Text["ModeDetail"] = _mode == ColourMode.Personal ? "Applying them also leaves the shared uniform." : "Everyone already using the uniform gets these colors too.";
            b.Text["IncludeSelected"] = _includeSelected ? "X" : "";
            b.Properties["IncludeSelected"] = new Dictionary<string, string> { ["Checked"] = _includeSelected ? "true" : "false" };
            if (_mode != ColourMode.Uniform) { b.Hidden.Add("IncludeSelected"); b.Hidden.Add("IncludeSelectedLabel"); }
            b.Text["Personal"] = (_mode == ColourMode.Personal ? "[Selected] " : "") + "Only this character";
            b.Text["Uniform"] = (_mode == ColourMode.Uniform ? "[Selected] " : "") + "Share a uniform";
            b.Text["Apply"] = _mode == ColourMode.Uniform ? "Apply shared uniform" : "Apply to character";
            b.Text["Status"] = _status;
            foreach (string key in new[] { "Primary", "Secondary" })
            {
                int rgb = key == "Primary" ? _palette.Primary : _palette.Secondary;
                b.Text[key + "Swatch"] = Quick.FirstOrDefault(q => q.RGB == rgb).Name ?? "Custom";
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

        private void BindProfile(UiBindings b)
        {
            var session = (WorldSession)s_session.GetValue(player)!;
            b.Text["Character"] = CharacterProfile.NameWithSelection(PersonalityUiBridge.Name(_frame), session.CurrentSnapshot.Characters.Count(c => c.IsPlayer && c.Selected));
            var colours = session.World.Systems.OfType<ColourSystem>().FirstOrDefault();
            ColourView? applied = colours?.Session == _target.Session ? colours.View.GetValueOrDefault(_target.Character) : null;
            var personality = session.World.Systems.OfType<PersonalitySystem>().FirstOrDefault();
            int tag = PersonalityUiBridge.Read(_frame)?.Tag ?? 0;
            if (personality is not null && personality.View.TryGetValue(_target.Character, out int published)) tag = published;
            b.Text["ProfilePersonality"] = "Personality: " + PersonalityOptions.Label(tag);
            var snapshot = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == _target.Character);
            if (snapshot is not null && colours?.Session == _target.Session) PersonalityUiBridge.ProfileColours(b, snapshot, applied);
            string[] fields = b.Text.GetValueOrDefault("chartweak3.profile.colours", _frame.Text("chartweak3.profile.colours")).Split('|');
            string mode = fields[0] switch { "personal" => "Personal", "uniform" => "Uniform", "equipment" => "Equipment", "none" => "Equipment (no dye)", _ => "Unavailable" };
            b.Text["ProfileColours"] = "Current colors: " + mode;
            _profilePalette = fields.Length == 3 && ColourPalette.TryHex(fields[1], out int primary) && ColourPalette.TryHex(fields[2], out int secondary)
                ? new(primary, secondary) : null;
            bool sameTarget = PersonalityUiBridge.TryGetEditorTarget(_frame, out _, out var id) && id == _target.Character;
            PortraitRendering.SetProfile(_frame, sameTarget);
            if (!sameTarget) b.Hidden.Add(CharacterProfile.ImageName);
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
                _includeSelected = false;
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
                if (c.Name == "ProfileFrame") background = new(0.16f, 0.16f, 0.14f, 1);
                if (c.Name == "IncludeSelected") background = new(0.16f, 0.16f, 0.14f, 1);
                if (c.Name == "IncludeSelectedLabel") background = Vector4.Zero;
                if (c.Name.EndsWith("@Track", StringComparison.Ordinal)) background = new(0.65f, 0.56f, 0.39f, 1);
                bool swatch = c.Name is "PrimarySwatch" or "SecondarySwatch";
                bool profileSwatch = c.Name is "ProfilePrimary" or "ProfileSecondary";
                bool selected = c.Name == "Personal" && _mode == ColourMode.Personal || c.Name == "Uniform" && _mode == ColourMode.Uniform;
                if (selected) background = new(0.39f, 0.34f, 0.22f, 1);
                if (swatch || profileSwatch)
                {
                    var palette = profileSwatch ? _profilePalette ?? new ColourPalette(0x555555, 0x555555) : _palette;
                    int rgb = c.Name is "PrimarySwatch" or "ProfilePrimary" ? palette.Primary : palette.Secondary;
                    background = new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
                }
                var text = selected ? new Vector4(1, .88f, .62f, 1) : new Vector4(0.92f, 0.92f, 0.88f, 1);
                if (swatch)
                {
                    // Black or white text keeps the name readable on both quick and custom colors.
                    float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
                    float luminance = .2126f * Linear(background.X) + .7152f * Linear(background.Y) + .0722f * Linear(background.Z);
                    text = luminance > .179f ? new Vector4(0, 0, 0, 1) : Vector4.One;
                }
                controls[i] = c with { Background = background, HoverColour = c.Name == "IncludeSelectedLabel" ? Vector4.Zero : swatch ? background : new Vector4(0.36f, 0.33f, 0.27f, 1),
                    ActiveColour = c.Name == "IncludeSelectedLabel" ? Vector4.Zero : swatch ? background : c.ActiveColour,
                    TextColour = text, DisabledColour = new Vector4(0.13f, 0.13f, 0.12f, 1),
                    Pickable = c.Name == "ColourWindow" ? true : c.Pickable,
                    Argument = c.Name == CharacterProfile.ImageName ? _frame.Text("chartweak3.profile") : c.Argument,
                    TextScale = c.Name == "Character" ? 1.15f : c.TextScale };
            }
        }

        private NamedPalette? Selected() => _preset >= 0 && _preset < library.Presets.Count ? library.Presets[_preset] : null;
        private void Close() { PortraitRendering.SetProfile(_frame, false); screens.Close(ColourLayout.Screen); s_pause.Invoke(player, null); }

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
                case "IncludeSelected": case "IncludeSelectedLabel":
                    if (_mode == ColourMode.Uniform) _includeSelected = !_includeSelected;
                    break;
                case "Advanced":
                    var bounds = screens.LastFrame.FirstOrDefault(f => f.Id == ColourLayout.Screen)?.Controls.FirstOrDefault(c => c.Name == "ColourWindow")?.Bounds;
                    if (bounds is { } window) _windowOrigin = new Vector2(window.X, window.Y) - screens.GetOffset(ColourLayout.Screen);
                    _advanced = !_advanced;
                    screens.Blur();
                    screens.Replace(ColourLayout.Screen, ColourLayout.Create(_advanced));
                    break;
                case "Personal": case "Uniform":
                    if (_mode == ColourMode.Personal) _personal = _palette; else _uniform = _palette;
                    _mode = e.Widget == "Personal" ? ColourMode.Personal : ColourMode.Uniform;
                    if (_mode == ColourMode.Personal) _includeSelected = false;
                    _palette = _mode == ColourMode.Personal ? _personal : _uniform;
                    SyncFields(); _status = "Choose colors, then Apply.";
                    break;
                case "PrimarySwatch": case "SecondarySwatch":
                    bool primary = e.Widget.StartsWith("Primary", StringComparison.Ordinal);
                    int current = Array.FindIndex(Quick, q => q.RGB == (primary ? _palette.Primary : _palette.Secondary));
                    int step = 1;
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
                    if (e.Widget == "Apply" && _mode == ColourMode.Uniform && _includeSelected)
                    {
                        var session = (WorldSession)s_session.GetValue(player)!;
                        foreach (var member in session.CurrentSnapshot.Characters.Where(c => c.Selected && c.IsPlayer
                            && c.Body is not { Dead: true } && c.Appearance is { Animal: null }).Select(c => c.Id).Distinct())
                            if (member != id) context.Enqueue(new SetColours(_target.Session, member, ColourMode.Uniform, _palette) { Tick = context.Tick });
                    }
                    _pending = new(e.Widget == "Reset" ? ColourMode.Original : _mode, _palette);
                    _pendingDraft = _palette; _pendingMode = _mode;
                    _status = "Colors queued.";
                    break;
                case "Back": Close(); _back(); break;
                case "Cancel": Close(); break;
            }
        }
    }
}
