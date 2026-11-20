using System.Collections;
using System.Reflection;
using HarmonyLib;

internal static class PaletteLibraryChecks
{
    internal static void Run(Assembly mod)
    {
        var type = mod.GetType("CharTweak3.PaletteLibrary", true)!;
        var palette = mod.GetType("CharTweak3.ColourPalette", true)!;
        string directory = Path.Combine(Path.GetTempPath(), "chartweak3-palettes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "palettes.json");
            var logs = new List<string>();
            object Open(string p) => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [p, (Action<string>)logs.Add], null)!;
            bool Save(object library, string name, int primary, bool overwrite = false) =>
                (bool)AccessTools.Method(type, "Save").Invoke(library, [name, Activator.CreateInstance(palette, primary, 0x123456), overwrite, null])!;
            object[] Entries(object library) => ((IEnumerable)AccessTools.Property(type, "Presets").GetValue(library)!).Cast<object>().ToArray();
            var library = Open(path);
            Check(Save(library, "Scouts #1", 0x112233), "first named preset saves");
            Check(!Save(library, "scouts #1", 0xFFFFFF), "case-insensitive duplicate cannot silently overwrite");
            Check(Save(library, "Scouts #1", 0x445566, true), "explicit overwrite saves");
            library = Open(path);
            var entry = Entries(library).Single();
            var colors = entry.GetType().GetProperty("Colours")!.GetValue(entry)!;
            Check((int)palette.GetProperty("Primary")!.GetValue(colors)! == 0x445566, "new process/library restores exact RGB values");
            Check(Save(library, "../紅隊", 0x778899) && Directory.GetFiles(directory).Length == 1, "Unicode and path-like preset names remain JSON data");
            Check(!Save(library, "bad\nname", 0x112233) && !Save(library, "valid", -1), "invalid names and RGB values rejected");
            Check((bool)AccessTools.Method(type, "Delete").Invoke(library, ["Scouts #1", null])! && Entries(Open(path)).Length == 1,
                "deletion persists without losing other presets");
            File.WriteAllText(path, "{broken json");
            var broken = Open(path);
            Check(!Save(broken, "Replacement", 0x112233) && File.ReadAllText(path) == "{broken json" && logs.Count > 0,
                "corrupt preset file is reported and preserved");
            string blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "file");
            var unavailable = Open(Path.Combine(blocked, "palettes.json"));
            Check(!Save(unavailable, "No write", 0x112233) && Entries(unavailable).Length == 0, "failed disk write leaves in-memory library unchanged");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic edits leave no temporary files");
            Console.WriteLine("PASS: preset disk reload, exact RGB, duplicate/overwrite/delete, Unicode names, malformed data and failed-write preservation.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
