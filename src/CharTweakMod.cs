using Meitou.Modding;
using Meitou.Simulation;

namespace CharTweak3;

/// <summary>
/// Entry point constructed by MeitouClient's code-mod loader.
/// </summary>
public sealed class CharTweakMod : IMod
{
    /// <summary>
    /// Runs once at startup, after data loading and before the first world is created.
    /// Register feature systems, UI extensions and any Harmony patches here as they are ported.
    /// </summary>
    public void Initialize(ModContext context)
    {
        PersonalityUiBridge.Install(context.Harmony);
        PersonalitySave.Install(context.Harmony);
        CharacterEditorLauncher.Initialize();
        CreationPersonality.Install(context.Harmony);
        ColourSave.Install(context.Harmony);
        ColourAppearance.Install(context.Harmony);
        var portraits = new PortraitFiles(context.LogError);
        PortraitSave.Install(context.Harmony);
        PortraitRendering.Install(context.Harmony);
        MainPanelDrag.Initialize(context.Harmony);
        ColourEditor.Install(context);
        ColourPausedUpdates.Install(context.Harmony);
        context.AddSystem("personalities", build => new PersonalitySystem(context.Data, build.Data.Factions), SystemAnchor.End);
        context.AddSystem("colours", _ => new ColourSystem(), SystemAnchor.End);
        context.AddSystem("portraits", _ => new PortraitSystem(portraits), SystemAnchor.End);
        context.AddUi(new CharTweakUi(context, portraits));
        context.Log($"CharTweak 3 {context.Manifest.Version}: personalities, character editor, colors, uniforms and custom portraits registered.");
    }
}
