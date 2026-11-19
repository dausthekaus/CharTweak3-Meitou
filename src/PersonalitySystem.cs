using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;

namespace CharTweak3;

/// <summary>
/// A change addressed to the character and world visible when Apply was pressed.
/// </summary>
internal sealed record SetPersonality(string Session, CharacterId Character, int Tag) : SimCommand;

/// <summary>
/// Applies edits serially and publishes immutable selection data for the UI.
/// </summary>
internal sealed class PersonalitySystem(GameDatabase database, IReadOnlyList<FactionData> factions) : ITickSystem, IWorldReady, IStateHashed
{
    /// <summary>
    /// The world whose edited values are included in the system's state hash; never read by the UI.
    /// </summary>
    private World? _world;

    /// <summary>
    /// Immutable after publication; UI code never reads the live character table.
    /// </summary>
    private Dictionary<CharacterId, int> _view = [];

    /// <summary>
    /// UI-only world identity, excluded from saves and deterministic gameplay hashes.
    /// </summary>
    internal string Session { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Published personalities for selected and inspected characters.
    /// </summary>
    internal IReadOnlyDictionary<CharacterId, int> View => Volatile.Read(ref _view);

    /// <summary>
    /// Publishes loaded/new characters before the first tick, including when the game starts paused.
    /// </summary>
    public void Ready(World world, bool fromSave)
    {
        _world = world;
        RefreshView(world);
    }

    /// <summary>
    /// Applies only valid edits for this world, never a replaced character occupying an old slot.
    /// </summary>
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _world = world;
        foreach (var command in commands)
        {
            if (command is not SetPersonality change || change.Session != Session || PersonalityOptions.IndexOf(change.Tag) < 0) continue;
            if (!world.Characters.TryResolveNext(change.Character, out int slot) || world.Characters.Cold(slot) is not { } cold) continue;
            if (cold.Medical?.Dead == true) continue;

            cold.Personality = change.Tag;
            PersonalitySave.Mark(cold);
        }
        RefreshView(world);
    }

    /// <summary>
    /// Publishes the finished tick's selection and personality values.
    /// </summary>
    public void SlowWorld(World world) => RefreshView(world);

    /// <summary>
    /// Copies selection data at a serial boundary; deriving an unrolled personality does not change vanilla state.
    /// </summary>
    internal void RefreshView(World world)
    {
        var view = new Dictionary<CharacterId, int>();
        foreach (var id in world.Player.Selection.Append(world.Player.Inspected).Distinct())
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold) continue;
            if (cold.Medical?.Dead == true) continue;
            string? faction = (uint)cold.Faction < (uint)factions.Count ? factions[cold.Faction].Id : null;
            view[id] = cold.Personality > 0 ? cold.Personality
                : Personality.Roll(database, cold.RecordId, faction, Rng.Hash(world.Seed, Rng.Key(id), RngPurpose.Dialogue, 0x9E75));
        }
        Volatile.Write(ref _view, view);
    }

    /// <summary>
    /// Hashes explicit saved/edited values because vanilla treats Personality as a derived value and does not hash it.
    /// </summary>
    public void Hash(ref StateHasher hasher)
    {
        if (_world is null) return;
        var table = _world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            var id = table.IdOf(slot);
            if (!table.IsAlive(id) || table.Cold(slot) is not { } cold || !PersonalitySave.IsMarked(cold)) continue;
            hasher.Add(id);
            hasher.Add(cold.Personality);
        }
    }
}
