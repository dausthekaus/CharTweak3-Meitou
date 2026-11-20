using System.Globalization;
using Meitou.Game.Ui;

namespace CharTweak3;

/// <summary>Read-only profile UI. Its swatches describe applied state, independently of any personality/color draft.</summary>
internal sealed class CharacterProfile(UiRegistration registration)
{
    internal const string ImageName = "chartweak3-meitou:profile-portrait";

    internal void Add(UiNode window, UiFrameContext frame, PersonalityTarget? target, float width, bool compact)
    {
        string name = target?.Name ?? PersonalityUiBridge.Name(frame);
        if (name.Length > 32) name = name[..29] + "...";
        float left = compact ? 60 : 136;
        window.Children.Add(Label("name", name, new(width - left - 16, compact ? 16 : 26, left, compact ? 28 : 44), "profile-name"));
        string personality = target is null ? "Select a living character." : $"Current: {PersonalityOptions.Label(target.Tag)}";
        window.Children.Add(Label("current", personality, new(width - left - 12, compact ? 16 : 22, left, compact ? 44 : 76)));
        float size = compact ? 44 : 104, top = compact ? 28 : 44, portraitLeft = compact ? 8 : 16;
        var portrait = new UiNode(registration.Key("profile-frame"))
        {
            Placement = new(size, size, portraitLeft, top), Style = registration.Key("profile-frame"), Pickable = false,
        };
        string key = frame.Text("chartweak3.profile");
        if (key.Length > 0 && frame.Text("chartweak3.profile.look") == "true")
            portrait.Children.Add(new UiNode(ImageName, UiNodeKind.Image) { Placement = UiPlacement.Fill, Argument = key });
        else portrait.Children.Add(Label("profile-empty", "—", UiPlacement.Fill, "label"));
        window.Children.Add(portrait);

        var fields = frame.Text("chartweak3.profile.colours").Split('|');
        bool known = fields.Length == 3 && int.TryParse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            && int.TryParse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        string mode = fields[0];
        string caption = mode switch { "personal" => "Personal", "uniform" => "Uniform", "equipment" => "Equipment", "none" => "Equipment (no dye)", _ => "Unavailable" };
        window.Children.Add(Label("profile-colours", compact ? caption : "Colors: " + caption,
            new(compact ? width - left - 76 : width - left - 16, compact ? 16 : 18, left, compact ? 60 : 104)));
        int primary = known ? int.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0x555555;
        int secondary = known ? int.Parse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0x555555;
        float swatchSize = compact ? 16 : 20;
        Swatch("profile-primary", compact ? width - 60 : left, compact ? 60 : 128, primary);
        Swatch("profile-secondary", compact ? width - 36 : left + 116, compact ? 60 : 128, secondary);
        if (!compact)
        {
            window.Children.Add(Label("profile-primary-hex", known ? "#" + fields[1] : "—", new(76, 20, left + 28, 128)));
            window.Children.Add(Label("profile-secondary-hex", known ? "#" + fields[2] : "—", new(76, 20, left + 144, 128)));
        }
        void Swatch(string id, float x, float y, int rgb) => window.Children.Add(new UiNode(registration.Key(id))
        { Placement = new(swatchSize, swatchSize, x, y), Style = registration.Key(id), Pickable = false, Argument = ColourPalette.Hex(rgb) });
    }

    private UiNode Label(string name, string text, UiPlacement placement, string style = "profile-text") =>
        new(registration.Key(name), UiNodeKind.Label) { Placement = placement, Style = registration.Key(style), Text = text };
}
