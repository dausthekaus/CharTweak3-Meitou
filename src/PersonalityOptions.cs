namespace CharTweak3;

/// <summary>
/// Named Kenshi personality tags; the gaps are unused tags, not consecutive menu indices.
/// </summary>
internal static class PersonalityOptions
{
    /// <summary>
    /// The ten choices exposed by CharTweak2, with their original tag numbers.
    /// </summary>
    internal static readonly (int Tag, string Label)[] All =
    [
        (1, "Honorable"), (2, "Traitorous"), (5, "Smart"), (6, "Dumb"),
        (9, "Brave"), (10, "Fearful"), (11, "Warm / Kind"), (12, "Cold / Cruel"),
        (13, "Normal"), (14, "Maniacal"),
    ];

    /// <summary>
    /// The menu index of a supported tag, or -1 for an unnamed tag.
    /// </summary>
    internal static int IndexOf(int tag) => Array.FindIndex(All, option => option.Tag == tag);

    /// <summary>
    /// A readable label without silently treating an unknown tag as Normal.
    /// </summary>
    internal static string Label(int tag) => IndexOf(tag) is >= 0 and var index ? All[index].Label : tag == 0 ? "None" : $"Tag {tag}";
}
