using System.Globalization;
using Meitou.Data.Ui;

namespace CharTweak3;

/// <summary>Native edit boxes and RGB sliders; relative child positions follow the host's viewport fitting.</summary>
internal static class ColourLayout
{
    internal const string Screen = "chartweak3-colours";
    private const float Width = 600, Height = 640;
    internal static MyGuiLayout Create()
    {
        var children = new List<MyGuiWidget>();
        void Label(string name, string caption, float x, float y, float w, float h = 24) =>
            children.Add(Widget("TextBox", name, x, y, w, h, new() { ["Caption"] = caption, ["TextAlign"] = "Center VCenter" }));
        void Button(string name, string caption, float x, float y, float w, float h = 30) =>
            children.Add(Widget("Button", name, x, y, w, h, new() { ["Caption"] = caption, ["TextAlign"] = "Center VCenter" }));
        void Edit(string name, float x, float y, float w, int limit) =>
            children.Add(Widget("EditBox", name, x, y, w, 28,
                new() { ["MaxTextLength"] = limit.ToString(), ["MultiLine"] = "false", ["TextAlign"] = "Center VCenter", ["DefaultAction"] = "false" }));
        Label("Title", "CharTweak 3 — Clothing colors (drag here)", 16, 12, 568, 28);
        Label("Character", "", 16, 44, 568);
        Button("Personal", "Personal", 24, 78, 264);
        Button("Uniform", "Uniform", 312, 78, 264);
        Label("ModeHint", "", 16, 112, 568);
        foreach (bool primary in new[] { true, false })
        {
            string key = primary ? "Primary" : "Secondary";
            float x = primary ? 24 : 312;
            Label(key + "Label", key, x, 144, 264);
            Button(key + "Swatch", "", x, 174, 264, 40);
            Label(key + "HexLabel", "Hex #", x, 224, 56, 28);
            Edit(key + "Hex", x + 64, 224, 200, 7);
            for (int channel = 0; channel < 3; channel++)
            {
                string name = key + "RGB"[channel];
                float y = 268 + channel * 34;
                Label(name + "Label", "RGB"[channel].ToString(), x, y, 20);
                children.Add(Slider(name, x + 28, y, 190, 24));
                Label(name + "Value", "0", x + 224, y, 40);
            }
            Button(key + "Previous", "<", x, 378, 30);
            Label(key + "Quick", "Custom", x + 36, 378, 192, 30);
            Button(key + "Next", ">", x + 234, 378, 30);
        }
        Label("PresetsLabel", "Saved palettes", 24, 422, 120, 30);
        Button("PresetPrevious", "<", 148, 422, 30);
        Label("Preset", "No saved palettes", 184, 422, 264, 30);
        Button("PresetNext", ">", 454, 422, 30);
        Button("LoadPreset", "Load", 492, 422, 84);
        Label("NameLabel", "Preset name", 24, 466, 120, 28);
        Edit("PresetName", 148, 466, 264, 32);
        Button("SavePreset", "Save new", 424, 466, 152, 28);
        Button("OverwritePreset", "Overwrite selected", 148, 504, 208, 28);
        Button("DeletePreset", "Delete selected", 368, 504, 208, 28);
        Label("Hint", "Dyeable clothing only. Apply keeps this window open.", 16, 542, 568, 22);
        Label("Status", "", 16, 568, 568, 22);
        Button("Apply", "Apply colors", 24, 602, 174);
        Button("Reset", "Reset original", 212, 602, 176);
        Button("Cancel", "Close", 402, 602, 174);
        var root = new MyGuiWidget("Window", "ColourWindow", "", "", "", new(0, 0, Width, Height), false,
            new Dictionary<string, string> { ["Movable"] = "true" }, new Dictionary<string, string>(), children);
        return new MyGuiLayout("", [root]);
    }

    private static MyGuiWidget Widget(string type, string name, float x, float y, float width, float height,
        Dictionary<string, string>? properties = null) =>
        new(type, name, "", "", "", new(x / Width, y / Height, width / Width, height / Height), true,
            properties ?? new(), new Dictionary<string, string>(), []);

    private static MyGuiWidget Slider(string name, float x, float y, float width, float height)
    {
        MyGuiWidget Part(string part, float w = 1) => new(part == "TrackPlace" ? "Widget" : "Button", name + "@" + part,
            "", "", "", new(0, 0, w, 1), true,
            new Dictionary<string, string> { ["TemplateOwner"] = name, ["TemplatePart"] = part,
                ["TemplateRealPosition"] = "0 0 " + w.ToString(CultureInfo.InvariantCulture) + " 1" }, new Dictionary<string, string>(), []);
        return Widget("ScrollBar", name, x, y, width, height,
            new() { ["Range"] = "256", ["VerticalAlignment"] = "false", ["MoveToClick"] = "true", ["Page"] = "1", ["ViewPage"] = "16" })
            with { Children = [Part("TrackPlace"), Part("FirstPart"), Part("SecondPart"), Part("Track", 0.08f)] };
    }
}
