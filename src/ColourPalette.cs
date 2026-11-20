using System.Globalization;
using Meitou.Data;
using Meitou.Data.Fcs;

namespace CharTweak3;

/// <summary>Two Kenshi dye channels, stored as RGB24 rather than transient database IDs.</summary>
internal sealed record ColourPalette(int Primary, int Secondary)
{
    internal static ColourPalette Default { get; } = new(0xB42D23, 0x232323);
    internal bool Valid => Primary is >= 0 and <= 0xFFFFFF && Secondary is >= 0 and <= 0xFFFFFF;
    internal static string Hex(int rgb) => rgb.ToString("X6", CultureInfo.InvariantCulture);

    internal static bool TryHex(string text, out int rgb)
    {
        text = text.Trim();
        if (text.StartsWith('#')) text = text[1..];
        rgb = 0;
        return text.Length == 6 && int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>A private immutable record for the renderer; never added to the game's shared database or inventory.</summary>
    internal GameRecord Dye()
    {
        var file = new FcsFile();
        var record = new FcsRecord
        {
            StringId = $"chartweak3-dye-{Hex(Primary)}-{Hex(Secondary)}", Name = "CharTweak personal palette",
            RecordType = FcsRecordType.ColorData,
        };
        record.Ints["color 1"] = Primary;
        record.Ints["color 2"] = Secondary;
        file.Records.Add(record);
        var privateDatabase = new GameDatabase();
        privateDatabase.Apply(file, "chartweak3-private.mod");
        return privateDatabase.Find(record.StringId)!;
    }
}

internal enum ColourMode { Original, Personal, Uniform }
internal sealed record ColourAssignment(ColourMode Mode, ColourPalette Palette);
