using System.Text;
using System.Text.Json;

namespace CharTweak3;

internal sealed record NamedPalette(string Name, ColourPalette Colours);

/// <summary>User-wide named palettes. All names are JSON data, never paths; edits replace the file atomically.</summary>
internal sealed class PaletteLibrary
{
    private sealed record Document(int Version, NamedPalette[] Presets);
    private const int Limit = 64, MaximumBytes = 65536;
    private readonly string _path;
    private readonly Action<string> _log;
    private NamedPalette[] _presets = [];
    private bool _unreadable;
    internal IReadOnlyList<NamedPalette> Presets => _presets;
    internal string? Error { get; private set; }

    internal static string DefaultPath()
    {
        string? config = Environment.GetEnvironmentVariable("CHARTWEAK3_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config)) config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config) || !Path.IsPathFullyQualified(config))
            config = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(config, "chartweak3-meitou", "palettes.json");
    }

    internal PaletteLibrary(string path, Action<string> log)
    {
        _path = path; _log = log;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Preset file exceeds 64 KiB.");
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is not { Version: 1, Presets: not null } || document.Presets.Length > Limit
                || document.Presets.Any(p => p is null || !ValidName(p.Name) || p.Colours?.Valid != true)
                || document.Presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Presets.Length)
                throw new InvalidDataException("Invalid preset format or duplicate names.");
            _presets = document.Presets;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _unreadable = true;
            Error = "Cannot read presets; original file preserved. See log.";
            _log($"{Error} {e.Message}");
        }
    }

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim()
        && name.Length <= 32 && !name.Any(char.IsControl);

    internal bool Save(string name, ColourPalette colours, bool overwrite, out string status)
    {
        name = name.Trim();
        if (!ValidName(name) || !colours.Valid) { status = "Use a preset name of 1–32 characters."; return false; }
        int index = Array.FindIndex(_presets, p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && !overwrite) { status = "Name already exists. Use Overwrite."; return false; }
        if (index < 0 && overwrite) { status = "Select an existing preset to overwrite."; return false; }
        if (index < 0 && _presets.Length >= Limit) { status = "Preset limit reached (64)."; return false; }
        var next = _presets.ToList();
        if (index >= 0) next[index] = new(name, colours);
        else next.Add(new(name, colours));
        return Store(next.ToArray(), "Preset saved.", out status);
    }

    internal bool Delete(string name, out string status)
    {
        if (!_presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { status = "Select a preset first."; return false; }
        return Store(_presets.Where(p => !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray(), "Preset deleted.", out status);
    }

    private bool Store(NamedPalette[] next, string success, out string status)
    {
        if (_unreadable) { status = Error!; return false; }
        string? temporary = null;
        try
        {
            string json = JsonSerializer.Serialize(new Document(1, next), new JsonSerializerOptions { WriteIndented = true });
            if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Preset file exceeds 64 KiB.");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
            _presets = next;
            status = success;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            status = "Could not save presets. See log.";
            _log($"{status} {e.Message}");
            return false;
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log($"Preset temporary file: {e.Message}"); }
        }
    }
}
