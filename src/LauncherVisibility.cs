using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>One native setting shared by every HUD registration, with a portable preference for clients predating programmatic setting persistence.</summary>
internal sealed class LauncherVisibility
{
    private sealed class Options
    {
        [Setting("Show CharTweak button", Hint = "Show the launcher at the upper left. Its keyboard shortcut also toggles this setting.")]
        public bool ShowLauncher { get; set; } = true;
    }

    private readonly Options _options = new();
    private readonly ModSetting _setting;
    private static LauncherVisibility? s_compatibility;
    private sealed record Preference(int Version, bool ShowLauncher);
    private readonly Action<string> _log;
    private bool _readOnly;
    internal static string PreferencePath => Path.Combine(Path.GetDirectoryName(PaletteLibrary.DefaultPath())!, "interface.json");
    internal bool Visible => _options.ShowLauncher;

    internal LauncherVisibility(ModContext context)
    {
        _log = context.LogError;
        var settings = context.AddSettings(_options);
        _setting = settings.Settings.Single(s => s.Key == nameof(Options.ShowLauncher));
        // 0.22.1 persists only changes made on the settings page. Keep shortcut/button changes too.
        bool? saved = ReadPreference();
        if (saved is { } show) _setting.Set(show);
        settings.Changed += setting => { if (setting == _setting) SavePreference(); };
        var add = AccessTools.DeclaredMethod(typeof(ModContext), "AddKeyAction");
        if (add is not null)
        {
            var parameters = add.GetParameters();
            if (parameters.Length != 4 || parameters[0].ParameterType != typeof(string) || parameters[1].ParameterType != typeof(string)
                || !parameters[2].ParameterType.IsGenericType || parameters[2].ParameterType.GetGenericTypeDefinition() != typeof(Action<>)
                || parameters[3].ParameterType != typeof(Binding[])) throw new NotSupportedException("Unsupported Meitou key-action registration API.");
            var callback = GetType().GetMethod(nameof(Pressed), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(parameters[2].ParameterType.GenericTypeArguments[0]).CreateDelegate(parameters[2].ParameterType, this);
            add.Invoke(context, ["toggle-launcher", "Show/hide CharTweak button", callback, new Binding[] { Key.Backslash }]);
        }
        else
        {
            s_compatibility = this;
            var tick = AccessTools.DeclaredMethod(typeof(WorldSession), nameof(WorldSession.Tick), Type.EmptyTypes);
            if (tick?.ReturnType != typeof(ActionState)) throw new NotSupportedException("Unsupported Meitou compatibility input API.");
            context.Harmony.Patch(tick, postfix: new HarmonyMethod(typeof(LauncherVisibility), nameof(CompatibilityKey)));
            context.Log("Launcher: backslash compatibility key on this client; rebindable Controls action available on clients with AddKeyAction.");
        }
    }

    internal void Toggle() => _setting.Set(!Visible);
    private void Pressed<T>(T context) => Toggle();

    internal bool? ReadPreference()
    {
        try
        {
            if (!File.Exists(PreferencePath)) return null;
            if (new FileInfo(PreferencePath).Length > 4096) throw new InvalidDataException("Interface preference is too large.");
            var preference = JsonSerializer.Deserialize<Preference>(File.ReadAllText(PreferencePath));
            if (preference is not { Version: 1 }) throw new InvalidDataException("Invalid interface preference.");
            return preference.ShowLauncher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { _readOnly = true; _log($"Interface preference unreadable; original preserved: {e.Message}"); return null; }
    }

    private void SavePreference()
    {
        if (_readOnly) return;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            temporary = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(1, Visible)));
            File.Move(temporary, PreferencePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { _log($"Interface preference could not be saved: {e.Message}"); }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Keep the in-memory preference. */ }
        }
    }

    private static void CompatibilityKey(WorldSession __instance, ActionState __result)
    {
        if (s_compatibility is null || !__result.Input.WasPressed(Key.Backslash)
            || !PersonalityUiBridge.CanUseLauncherKey(__instance)) return;
        // Respect keys already assigned by the player. The host filters text fields, settings and console input before this point.
        if (Enum.GetValues<InputAction>().Any(a => __instance.Bindings.Get(a).Contains(Binding.Of(Key.Backslash)))) return;
        s_compatibility.Toggle();
    }
}
