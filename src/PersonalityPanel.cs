using Meitou.Game.Ui;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>
/// UI-thread-only window state. It reads frame data and queues edits without touching the live world.
/// </summary>
internal sealed class PersonalityPanel(UiRegistration registration, ModContext context)
{
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
    internal void Close(UiActionContext action) => _open = false;

    /// <summary>
    /// Opens the game's editor and dismisses this panel only when the editor successfully opens.
    /// </summary>
    internal void OpenEditor(UiActionContext action)
    {
        if (CharacterEditorLauncher.Open(action.Frame, out string status)) _open = false;
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
        var root = new UiNode(registration.Key("overlay")) { Pickable = false };
        root.Children.Add(Button("open", "CharTweak 3", new UiPlacement(144, 36, 16, 96)));
        if (_open) root.Children.Add(Window(frame, target));
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
        float height = Math.Min(320, Math.Max(0, frame.Viewport.Height - 32));
        bool compact = height < 280;
        var window = new UiNode(registration.Key("window"))
        {
            Placement = new UiPlacement(width, height, (frame.Viewport.Width - width) / 2, (frame.Viewport.Height - height) / 2),
            Style = registration.Key("window"),
        };
        string name = target?.Name ?? PersonalityUiBridge.Name(frame);
        if (name.Length > 32) name = name[..29] + "...";
        window.Children.Add(Label("title", "CharTweak 3", new UiPlacement(UiLength.Fill, 24, Top: compact ? 4 : 8)));
        window.Children.Add(Label("name", name, new UiPlacement(UiLength.Fill, 20, Top: compact ? 28 : 38)));
        string current = target is null ? "Select a living character." : $"Current: {PersonalityOptions.Label(target.Tag)}";
        window.Children.Add(Label("current", current, new UiPlacement(UiLength.Fill, 20, Top: compact ? 48 : 64)));
        float rowTop = compact ? 72 : 96;
        float rowHeight = compact ? 30 : 36;
        window.Children.Add(Button("previous", "<", new UiPlacement(36, rowHeight, 16, rowTop), target is not null));
        string choice = _index < 0 ? "Choose a personality" : PersonalityOptions.All[_index].Label;
        window.Children.Add(Label("choice", choice, new UiPlacement(width - 120, rowHeight, 60, rowTop), "choice"));
        window.Children.Add(Button("next", ">", new UiPlacement(36, rowHeight, Top: rowTop, Right: 16), target is not null));
        window.Children.Add(Label("status", _status, new UiPlacement(UiLength.Fill, 22, Top: compact ? 104 : 140)));
        window.Children.Add(Button("editor", "Open Character Editor",
            new UiPlacement(width - 32, compact ? 28 : 36, 16, compact ? 128 : 184), PersonalityUiBridge.CanOpenEditor(frame)));
        bool canApply = target is not null && _index >= 0 && PersonalityOptions.All[_index].Tag != target.Tag
            && PersonalityOptions.All[_index].Tag != _pending;
        float actionHeight = compact ? 28 : 32;
        window.Children.Add(Button("apply", "Apply", new UiPlacement(112, actionHeight, width / 2 - 120, Bottom: 8), canApply));
        window.Children.Add(Button("close", "Close", new UiPlacement(112, actionHeight, width / 2 + 8, Bottom: 8)));
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
