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
        context.AddSystem("personalities", build => new PersonalitySystem(context.Data, build.Data.Factions), SystemAnchor.End);
        context.AddUi(new CharTweakUi(context));
        context.Log($"CharTweak 3 {context.Manifest.Version}: personality selector and character editor registered.");
    }
}
