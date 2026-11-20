using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace CharTweak3;

/// <summary>
/// Registers the selector in both HUD modes, with a separate panel state for each UI registration.
/// </summary>
internal sealed class CharTweakUi(ModContext context, PortraitFiles portraits, LauncherVisibility launcher) : IUiExtension
{
    /// <summary>
    /// Prefix for this extension's controls, styles and actions.
    /// </summary>
    public string Id => "chartweak3-meitou";

    /// <summary>
    /// Registers the native UI styles and panel actions.
    /// </summary>
    public void Register(UiRegistration registration)
    {
        registration.RegisterStyle("button", new UiStyle
        {
            Background = new Vector4(0.24f, 0.22f, 0.18f, 1),
            Hover = new Vector4(0.36f, 0.33f, 0.27f, 1),
            Active = new Vector4(0.16f, 0.15f, 0.12f, 1),
            Disabled = new Vector4(0.15f, 0.15f, 0.14f, 1),
            TextAlign = "Center VCenter",
        });
        registration.RegisterStyle("window", new UiStyle { Background = new Vector4(0.10f, 0.10f, 0.09f, 1) });
        registration.RegisterStyle("label", new UiStyle { TextAlign = "Center VCenter" });
        registration.RegisterStyle("choice", new UiStyle { TextAlign = "Center VCenter", TextScale = 1.2f });
        registration.RegisterStyle("profile-text", new UiStyle { TextAlign = "Left VCenter", TextScale = .9f });
        registration.RegisterStyle("profile-name", new UiStyle { TextAlign = "Left VCenter", TextScale = 1.15f });
        registration.RegisterStyle("profile-frame", new UiStyle { Background = new Vector4(.16f, .16f, .14f, 1) });
        registration.RegisterStyle("profile-primary", new UiStyle());
        registration.RegisterStyle("profile-secondary", new UiStyle());

        var panel = new PersonalityPanel(registration, context, portraits, launcher);
        registration.RegisterAction("open", panel.Open);
        registration.RegisterAction("close", panel.Close);
        registration.RegisterAction("previous", panel.Previous);
        registration.RegisterAction("next", panel.Next);
        registration.RegisterAction("apply", panel.Apply);
        registration.RegisterAction("editor", panel.OpenEditor);
        registration.RegisterAction("colours", panel.OpenColours);
        registration.RegisterAction("portrait", panel.ChoosePortrait);
        registration.RegisterAction("portrait-reset", panel.ResetPortrait);
        registration.RegisterAction("launcher", _ => launcher.Toggle());
        registration.Contribute(UiSlots.Overlay, panel.Build);
    }
}
