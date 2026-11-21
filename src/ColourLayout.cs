using System.Globalization;
using Meitou.Data.Ui;

namespace CharTweak3;

/// <summary>Two color columns with an applied profile and optional RGB controls.</summary>
internal static class ColourLayout
{
    internal const string Screen = "chartweak3-colours";
    private const float Width = 600;
    internal static MyGuiLayout Create(bool advanced = false)
    {
        float height = advanced ? 744 : 644;
        var children = new List<MyGuiWidget>();
        MyGuiWidget Widget(string type, string name, float x, float y, float w, float h, Dictionary<string, string>? properties = null) =>
            new(type, name, "", "", "", new(x / Width, y / height, w / Width, h / height), true,
                properties ?? new(), new Dictionary<string, string>(), []);
        void Label(string name, string caption, float x, float y, float w, float h = 24, string align = "Center VCenter") =>
            children.Add(Widget("TextBox", name, x, y, w, h, new() { ["Caption"] = caption, ["TextAlign"] = align }));
        void Button(string name, string caption, float x, float y, float w, float h = 30) =>
            children.Add(Widget("Button", name, x, y, w, h, new() { ["Caption"] = caption, ["TextAlign"] = "Center VCenter" }));
        void Edit(string name, float x, float y, float w, int limit) =>
            children.Add(Widget("EditBox", name, x, y, w, 28,
                new() { ["MaxTextLength"] = limit.ToString(), ["MultiLine"] = "false", ["TextAlign"] = "Center VCenter", ["DefaultAction"] = "false" }));
        Label("Title", "Clothing colors — drag here to move", 16, 10, 568, 28);
        children.Add(Widget("Widget", "ProfileFrame", 24, 44, 76, 76, new() { ["NeedMouse"] = "false" }));
        children.Add(Widget("ImageBox", CharacterProfile.ImageName, 24, 44, 76, 76, new() { ["NeedMouse"] = "false" }));
        Label("Character", "", 120, 44, 456, 26, "Left VCenter");
        Label("ProfilePersonality", "", 120, 74, 456, 20, "Left VCenter");
        Label("ProfileColours", "", 120, 100, 324, 20, "Left VCenter");
        children.Add(Widget("Widget", "ProfilePrimary", 456, 100, 20, 20, new() { ["NeedMouse"] = "false" }));
        children.Add(Widget("Widget", "ProfileSecondary", 488, 100, 20, 20, new() { ["NeedMouse"] = "false" }));
        Label("ModeLabel", "Who should use these colors?", 24, 128, 552, 22);
        Button("Personal", "Only this character", 24, 156, 264, 36);
        Button("Uniform", "Share a uniform", 312, 156, 264, 36);
        Label("ModeHint", "", 16, 198, 568, 20);
        Label("ModeDetail", "", 16, 220, 568, 20);
        Button("IncludeSelected", "", 24, 244, 24, 24);
        children.Add(Widget("Button", "IncludeSelectedLabel", 56, 244, 520, 24,
            new() { ["Caption"] = "Include all selected squad members", ["TextAlign"] = "Left VCenter" }));
        foreach (bool primary in new[] { true, false })
        {
            string key = primary ? "Primary" : "Secondary";
            float x = primary ? 24 : 312;
            Label(key + "Label", key, x, 278, 264, 22);
            Button(key + "Swatch", "", x, 304, 264, 40);
            Label(key + "HexLabel", "Hex #", x, 364, 56, 28);
            Edit(key + "Hex", x + 64, 364, 200, 7);
            if (!advanced) continue;
            for (int channel = 0; channel < 3; channel++)
            {
                string name = key + "RGB"[channel];
                float y = 438 + channel * 32;
                Label(name + "Label", "RGB"[channel].ToString(), x, y, 20);
                children.Add(Slider(name, x + 28, y, 190, 24));
                Label(name + "Value", "0", x + 224, y, 40);
            }
        }
        Label("SwatchHint", "Click either color square to cycle quick colors.", 16, 344, 568, 18);
        Button("Advanced", advanced ? "Advanced RGB controls: On" : "Advanced RGB controls: Off", 24, 400, 552, 28);
        float saved = advanced ? 538 : 438;
        Label("PresetsLabel", "Saved palettes", 24, saved, 120, 28);
        Button("PresetPrevious", "<", 148, saved, 30, 28);
        Label("Preset", "No saved palettes", 184, saved, 264, 28);
        Button("PresetNext", ">", 454, saved, 30, 28);
        Button("LoadPreset", "Load", 492, saved, 84, 28);
        Label("NameLabel", "Preset name", 24, saved + 36, 120, 28);
        Edit("PresetName", 148, saved + 36, 264, 32);
        Button("SavePreset", "Save new", 424, saved + 36, 152, 28);
        Button("OverwritePreset", "Overwrite selected", 148, saved + 72, 208, 28);
        Button("DeletePreset", "Delete selected", 368, saved + 72, 208, 28);
        Label("Hint", "Dyeable clothing only. Changes take effect when you Apply.", 16, saved + 106, 568, 20);
        Label("Status", "", 16, saved + 130, 568, 22);
        Button("Apply", "Apply to character", 24, saved + 162, 174);
        Button("Reset", "Restore original", 212, saved + 162, 176);
        Button("Back", "Back", 402, saved + 162, 82);
        Button("Cancel", "Close", 494, saved + 162, 82);
        var root = new MyGuiWidget("Window", "ColourWindow", "", "", "", new(0, 0, Width, height), false,
            new Dictionary<string, string> { ["Movable"] = "true" }, new Dictionary<string, string>(), children);
        return new MyGuiLayout("", [root]);

        MyGuiWidget Slider(string name, float x, float y, float width, float sliderHeight)
        {
            MyGuiWidget Part(string part, float w = 1) => new(part == "TrackPlace" ? "Widget" : "Button", name + "@" + part,
                "", "", "", new(0, 0, w, 1), true,
                new Dictionary<string, string> { ["TemplateOwner"] = name, ["TemplatePart"] = part,
                    ["TemplateRealPosition"] = "0 0 " + w.ToString(CultureInfo.InvariantCulture) + " 1" }, new Dictionary<string, string>(), []);
            return Widget("ScrollBar", name, x, y, width, sliderHeight,
                new() { ["Range"] = "256", ["VerticalAlignment"] = "false", ["MoveToClick"] = "true", ["Page"] = "1", ["ViewPage"] = "16" })
                with { Children = [Part("TrackPlace"), Part("FirstPart"), Part("SecondPart"), Part("Track", 0.08f)] };
        }
    }
}
