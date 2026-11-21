using Meitou.Game.Ui;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>
/// UI-thread-only window state. It reads frame data and queues edits without touching the live world.
/// </summary>
internal sealed class PersonalityPanel(UiRegistration registration, ModContext context, PortraitFiles portraits, LauncherVisibility launcher)
{
    private readonly PortraitActions _portraits = new(context, portraits);
    private readonly CharacterProfile _profile = new(registration);
    internal void ChoosePortrait(UiActionContext action) => _portraits.Choose(action);
    internal void ResetPortrait(UiActionContext action) => _portraits.Reset(action);
    internal void OpenPhoto(UiActionContext action)
    {
        _portraits.ClearStatus();
        if (_portraits.Busy) return;
        if (PhotoEditor.Open(action.Frame, () => _open = true, out string status)) { _open = false; MainPanelDrag.Hide(action.Frame); }
        _status = status;
    }
    /// <summary>
    /// Whether this panel is visible.
    /// </summary>
    private bool _open;

    /// <summary>
    /// The character/world the current preview belongs to.
    /// </summary>
    private string? _targetKey;

    /// <summary>
    /// Preview choice in PersonalityOptions.All, or -1 for no supported choice.
    /// </summary>
    private int _index = -1;

    /// <summary>
    /// The last requested tag, waiting to be observed in a published frame.
    /// </summary>
    private int? _pending;

    /// <summary>
    /// The current action's feedback.
    /// </summary>
    private string _status = "";

    /// <summary>
    /// Opens the panel.
    /// </summary>
    internal void Open(UiActionContext action) => _open = true;

    /// <summary>
    /// Closes the panel without changing the character.
    /// </summary>
    internal void Close(UiActionContext action)
    {
        _open = false;
        PortraitRendering.SetProfile(action.Frame, false);
        MainPanelDrag.Hide(action.Frame);
    }

    /// <summary>
    /// Opens the game's editor and dismisses this panel only when the editor successfully opens.
    /// </summary>
    internal void OpenEditor(UiActionContext action)
    {
        _portraits.ClearStatus();
        if (CharacterEditorLauncher.Open(action.Frame, out string status)) { _open = false; MainPanelDrag.Hide(action.Frame); }
        _status = status;
    }

    /// <summary>Opens the native color controls for the same revalidated player character.</summary>
    internal void OpenColours(UiActionContext action)
    {
        _portraits.ClearStatus();
        if (ColourEditor.Open(action.Frame, () => _open = true, out string status)) { _open = false; MainPanelDrag.Hide(action.Frame); }
        _status = status;
    }

    /// <summary>
    /// Previews the preceding named personality.
    /// </summary>
    internal void Previous(UiActionContext action) => Cycle(action.Frame, -1);

    /// <summary>
    /// Previews the next named personality.
    /// </summary>
    internal void Next(UiActionContext action) => Cycle(action.Frame, 1);

    /// <summary>
    /// Queues the preview for exactly the character and world shown in this frame.
    /// </summary>
    internal void Apply(UiActionContext action)
    {
        _portraits.ClearStatus();
        var target = PersonalityUiBridge.Read(action.Frame);
        Synchronize(target);
        if (target is null || _index < 0) return;
        int tag = PersonalityOptions.All[_index].Tag;
        if (tag == target.Tag || tag == _pending) return;
        context.Enqueue(new SetPersonality(target.Session, target.Character, tag) { Tick = context.Tick });
        _pending = tag;
        _status = "Queued. Unpause to apply.";
    }

    /// <summary>
    /// Builds the launcher and optional panel; empty overlay space passes clicks through.
    /// </summary>
    internal UiNode Build(UiFrameContext frame)
    {
        var target = PersonalityUiBridge.Read(frame);
        Synchronize(target);
        _portraits.Update(frame);
        var root = new UiNode(registration.Key("overlay")) { Pickable = false };
        PortraitRendering.SetProfile(frame, _open);
        if (launcher.Visible) root.Children.Add(Button("open", "CharTweak 3", new UiPlacement(144, 36, 16, 96)));
        if (_open) root.Children.Add(Window(frame, target));
        else MainPanelDrag.Hide(frame);
        return root;
    }

    /// <summary>
    /// Resets the preview when switching characters/worlds and acknowledges applied edits.
    /// </summary>
    private void Synchronize(PersonalityTarget? target)
    {
        if (_targetKey != target?.Key)
        {
            _targetKey = target?.Key;
            _index = target is null ? -1 : PersonalityOptions.IndexOf(target.Tag);
            _pending = null;
            _status = "";
        }
        if (target is not null && _pending == target.Tag)
        {
            _pending = null;
            _status = "Personality updated.";
        }
    }

    /// <summary>
    /// Cycles the preview only; no gameplay change occurs until Apply is pressed.
    /// </summary>
    private void Cycle(UiFrameContext frame, int direction)
    {
        _portraits.ClearStatus();
        var target = PersonalityUiBridge.Read(frame);
        Synchronize(target);
        if (target is null) return;
        _index = _index < 0 ? (direction > 0 ? 0 : PersonalityOptions.All.Length - 1)
            : (_index + direction + PersonalityOptions.All.Length) % PersonalityOptions.All.Length;
        _status = "Press Apply to change personality.";
    }

    /// <summary>
    /// Centers the selector, current personality and action buttons within the viewport.
    /// </summary>
    private UiNode Window(UiFrameContext frame, PersonalityTarget? target)
    {
        float width = Math.Min(480, Math.Max(0, frame.Viewport.Width - 32));
        float height = Math.Min(400, Math.Max(0, frame.Viewport.Height - 32));
        bool compact = height < 380;
        var bounds = MainPanelDrag.Place(frame, width, height);
        var window = new UiNode(registration.Key("window"))
        {
            Placement = new UiPlacement(width, height, bounds.X, bounds.Y),
            Style = registration.Key("window"),
        };
        window.Children.Add(Label("title", "CharTweak 3 (drag here)", new UiPlacement(UiLength.Fill, 24, Top: compact ? 4 : 8)));
        _profile.Add(window, frame, target, width, compact);
        float rowTop = compact ? 78 : 168;
        float rowHeight = compact ? 24 : 36;
        window.Children.Add(Button("previous", "<", new UiPlacement(36, rowHeight, 16, rowTop), target is not null));
        string choice = _index < 0 ? "Choose a personality" : PersonalityOptions.All[_index].Label;
        window.Children.Add(Label("choice", choice, new UiPlacement(width - 120, rowHeight, 60, rowTop), "choice"));
        window.Children.Add(Button("next", ">", new UiPlacement(36, rowHeight, Top: rowTop, Right: 16), target is not null));
        window.Children.Add(Label("status", _portraits.Status ?? _status, new UiPlacement(UiLength.Fill, compact ? 18 : 22, Top: compact ? 102 : 212)));
        window.Children.Add(Button("editor", compact ? "Editor" : "Character Editor",
            new UiPlacement((width - 48) / 2, compact ? 22 : 36, 16, compact ? 122 : 244), PersonalityUiBridge.CanOpenEditor(frame)));
        window.Children.Add(Button("colours", "Clothing Colors",
            new UiPlacement((width - 48) / 2, compact ? 22 : 36, width / 2 + 8, compact ? 122 : 244), ColourTarget.Read(frame) is not null));
        float portraitWidth = (width - 64) / 3;
        window.Children.Add(Button("portrait", _portraits.Busy ? "Importing..." : compact ? "Import" : "Import Portrait",
            new UiPlacement(portraitWidth, compact ? 22 : 36, 16, compact ? 146 : 288), _portraits.CanChoose(frame)));
        window.Children.Add(Button("photo", "Photo",
            new UiPlacement(portraitWidth, compact ? 22 : 36, 32 + portraitWidth, compact ? 146 : 288), !_portraits.Busy && PhotoEditor.CanOpen(frame)));
        window.Children.Add(Button("portrait-reset", "Reset Portrait",
            new UiPlacement(portraitWidth, compact ? 22 : 36, 48 + portraitWidth * 2, compact ? 146 : 288), _portraits.CanReset(frame)));
        bool canApply = target is not null && _index >= 0 && PersonalityOptions.All[_index].Tag != target.Tag
            && PersonalityOptions.All[_index].Tag != _pending;
        float actionHeight = compact ? 24 : 32;
        float buttonWidth = (width - 48) / 3;
        if (height >= 222)
            window.Children.Add(Label("launcher-hint", compact ? "\\: show/hide launcher (default)" : "Press \\ to show/hide the launcher (default shortcut).",
                new UiPlacement(width - 32, compact ? 18 : 20, 16, Bottom: compact ? 36 : 44), "profile-text"));
        window.Children.Add(Button("apply", "Apply", new UiPlacement(buttonWidth, actionHeight, 16, Bottom: 8), canApply));
        window.Children.Add(Button("launcher", launcher.Visible ? (compact ? "Hide (\\)" : "Hide button (\\)") : (compact ? "Show (\\)" : "Show button (\\)"),
            new UiPlacement(buttonWidth, actionHeight, 24 + buttonWidth, Bottom: 8)));
        window.Children.Add(Button("close", "Close", new UiPlacement(buttonWidth, actionHeight, 32 + buttonWidth * 2, Bottom: 8)));
        return window;
    }

    /// <summary>
    /// Builds a named text label.
    /// </summary>
    private UiNode Label(string name, string text, UiPlacement placement, string style = "label") =>
        new(registration.Key(name), UiNodeKind.Label) { Placement = placement, Style = registration.Key(style), Text = text };

    /// <summary>
    /// Builds a styled button bound to the action of the same name.
    /// </summary>
    private UiNode Button(string name, string text, UiPlacement placement, bool enabled = true) =>
        new(registration.Key(name), UiNodeKind.Button)
        {
            Placement = placement, Style = registration.Key("button"), Text = text,
            Action = registration.Key(name), Enabled = enabled,
        };
}
