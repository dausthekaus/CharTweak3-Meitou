using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>Captures the target before opening an asynchronous chooser and revalidates it on the UI thread.</summary>
internal sealed class PortraitActions(ModContext context, PortraitFiles files)
{
    private sealed record Result(PortraitAsset? Asset, string Status);
    private Task<Result>? _import;
    private string? _target, _session, _pending;
    private bool _waiting;
    internal string? Status { get; private set; }
    internal bool Busy => _import is not null || PortraitPicker.IsOpen || _waiting;
    internal void ClearStatus() { if (!Busy) Status = null; }
    private static readonly FieldInfo SessionField = AccessTools.Field(typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!, "Session");

    private static PortraitSystem? Current(UiFrameContext frame) => PersonalityUiBridge.InterfaceFor(frame) is { } ui
        && SessionField.GetValue(ui) is WorldSession session ? session.World.Systems.OfType<PortraitSystem>().FirstOrDefault() : null;

    internal bool CanChoose(UiFrameContext frame) => !Busy && Current(frame) is not null && PersonalityUiBridge.CanOpenEditor(frame);
    internal bool CanReset(UiFrameContext frame) => CanChoose(frame) && PersonalityUiBridge.Read(frame) is { } target
        && frame.Text("chartweak3.portrait.assigned") == target.Key;

    internal void Choose(UiActionContext action)
    {
        var frame = action.Frame; var target = PersonalityUiBridge.Read(frame); var system = Current(frame);
        if (Busy || target is null || system is null || !PersonalityUiBridge.TryGetEditorTarget(frame, out _, out var id) || id != target.Character) return;
        _target = target.Key; _session = system.Session; Status = "Choose an image in the OS file chooser...";
        _import = Import();
    }
    private async Task<Result> Import()
    {
        try
        {
            string? path = await PortraitPicker.Choose().ConfigureAwait(false);
            if (path is null) return new(null, "Portrait selection cancelled.");
            var asset = await Task.Run(() => files.Import(path)).ConfigureAwait(false);
            if (!File.Exists(asset.Path)) return new(null, "Cannot write portrait storage. See log.");
            return new(asset, "");
        }
        catch (Exception e)
        {
            context.LogError($"Portrait import failed: {e}");
            return new(null, e is IOException or InvalidDataException or PlatformNotSupportedException or InvalidOperationException
                ? e.Message : "Could not import that image. See log.");
        }
    }
    internal void Reset(UiActionContext action)
    {
        var target = PersonalityUiBridge.Read(action.Frame); var system = Current(action.Frame);
        if (!CanReset(action.Frame) || target is null || system is null
            || !PersonalityUiBridge.TryGetEditorTarget(action.Frame, out _, out var id) || id != target.Character) return;
        _target = target.Key; _session = system.Session;
        context.Enqueue(new SetPortrait(system.Session, target.Character, null) { Tick = context.Tick });
        _pending = null; _waiting = true; Status = "Restoring original portrait...";
    }
    internal void Update(UiFrameContext frame)
    {
        var target = PersonalityUiBridge.Read(frame); var system = Current(frame);
        if (_import is { IsCompleted: true })
        {
            var result = _import.GetAwaiter().GetResult(); _import = null;
            if (target is null || system is null || target.Key != _target || system.Session != _session
                || !PersonalityUiBridge.TryGetEditorTarget(frame, out _, out var id) || id != target.Character)
            { Status = "Selection changed; portrait was not applied."; return; }
            if (result.Asset is null) { Status = result.Status; return; }
            if (system.View.Values.Select(v => v.Id).Distinct().Count() >= PortraitSystem.MaximumAssets
                && !system.View.Values.Any(v => v.Id == result.Asset.Id))
            { Status = "Portrait limit reached (128 different images)."; return; }
            context.Enqueue(new SetPortrait(system.Session, target.Character, result.Asset) { Tick = context.Tick });
            _pending = result.Asset.Id; _waiting = true; Status = "Applying portrait...";
        }
        if (_waiting)
        {
            if (target is null || system is null || target.Key != _target || system.Session != _session)
            { _waiting = false; Status = null; return; }
            string? actual = system.View.GetValueOrDefault(target.Character)?.Id;
            if (actual == _pending) { _waiting = false; Status = _pending is null ? "Original portrait restored." : "Portrait applied."; }
        }
        else if (_import is null && target?.Key != _target) Status = null;
    }
}
