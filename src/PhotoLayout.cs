using Meitou.Data.Ui;

namespace CharTweak3;

internal static class PhotoLayout
{
    internal const string Screen = "chartweak3-photo", Texture = "chartweak3:photo-preview";
    internal static MyGuiLayout Create()
    {
        const float width = 600, height = 636;
        var children = new List<MyGuiWidget>();
        void Add(string type, string name, string caption, float x, float y, float w, float h, Dictionary<string, string>? properties = null)
        {
            var p = properties ?? new(); p["Caption"] = caption; p["TextAlign"] = "Center VCenter";
            children.Add(new(type, name, "", "", "", new(x / width, y / height, w / width, h / height), true, p, new Dictionary<string, string>(), []));
        }
        Add("TextBox", "Title", "CharTweak Photo — drag here to move", 16, 10, 568, 28);
        Add("TextBox", "Character", "", 16, 40, 568, 24);
        Add("ImageBox", "Background", "", 124, 74, 352, 352, new() { ["NeedMouse"] = "false" });
        Add("ImageBox", "Preview", "", 124, 74, 352, 352, new() { ["NeedMouse"] = "false", ["ImageTexture"] = Texture, ["ImageFlipV"] = "true" });
        Add("TextBox", "FocusLabel", "Focus", 16, 80, 100, 22);
        Add("Button", "FocusHead", "Head", 16, 108, 100, 30);
        Add("Button", "FocusBody", "Full body", 16, 146, 100, 30);
        Add("TextBox", "AngleLabel", "Angle", 16, 190, 100, 22);
        Add("Button", "AngleUp", "Angle up", 16, 218, 100, 30);
        Add("Button", "AngleDown", "Angle down", 16, 256, 100, 30);
        Add("TextBox", "PanLabel", "Move frame", 484, 80, 100, 22);
        Add("Button", "PanUp", "Up", 484, 108, 100, 30);
        Add("Button", "PanLeft", "Left", 484, 146, 100, 30);
        Add("Button", "PanRight", "Right", 484, 184, 100, 30);
        Add("Button", "PanDown", "Down", 484, 222, 100, 30);
        Add("Button", "ResetCamera", "Reset view", 484, 276, 100, 30);
        Add("Button", "ChooseBackground", "Choose background...", 24, 438, 264, 30);
        Add("Button", "ClearBackground", "Plain background", 312, 438, 264, 30);
        Add("Button", "TurnLeft", "Turn left", 24, 478, 126, 30);
        Add("Button", "TurnRight", "Turn right", 166, 478, 126, 30);
        Add("Button", "Closer", "Zoom in", 308, 478, 126, 30);
        Add("Button", "Further", "Zoom out", 450, 478, 126, 30);
        Add("TextBox", "Hint", "Current pose and gear. Picture saves a still portrait.", 16, 516, 568, 22);
        Add("TextBox", "Status", "", 16, 544, 568, 32);
        Add("Button", "Picture", "Picture", 24, 590, 264, 30);
        Add("Button", "Back", "Back", 312, 590, 126, 30);
        Add("Button", "Cancel", "Close", 450, 590, 126, 30);
        return new("", [new MyGuiWidget("Window", "PhotoWindow", "", "", "", new(0, 0, width, height), false,
            new Dictionary<string, string> { ["Movable"] = "true" }, new Dictionary<string, string>(), children)]);
    }
}
