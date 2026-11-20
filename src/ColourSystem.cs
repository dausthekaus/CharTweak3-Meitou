using System.Runtime.CompilerServices;
using System.Text.Json;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Data.Characters;

namespace CharTweak3;

internal sealed record SetColours(string Session, CharacterId Character, ColourMode Mode, ColourPalette Palette) : SimCommand;
internal sealed record ColourView(ColourMode Mode, ColourPalette Palette, ColourPalette Uniform);

/// <summary>Serial color commands, shared uniform state and immutable UI views, one instance per running world.</summary>
internal sealed class ColourSystem : ITickSystem, IWorldReady, IStateHashed, IModSaveData
{
    private sealed class Applied
    {
        internal CharacterAppearance? Original, Tinted;
        internal ColourPalette? Palette;
    }

    private readonly ConditionalWeakTable<CharacterCold, Applied> _applied = new();
    private Dictionary<CharacterId, ColourView> _view = [];
    private World? _world;
    private bool _hasUniform;
    private ColourPalette _uniform = ColourPalette.Default;
    internal string Session { get; } = Guid.NewGuid().ToString("N");
    internal IReadOnlyDictionary<CharacterId, ColourView> View => Volatile.Read(ref _view);

    public void Ready(World world, bool fromSave) { _world = world; Refresh(world); }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _world = world;
        foreach (var command in commands)
        {
            if (command is not SetColours edit || edit.Session != Session || edit.Palette is null || !edit.Palette.Valid
                || edit.Mode is < ColourMode.Original or > ColourMode.Uniform
                || !world.Characters.TryResolveNext(edit.Character, out int slot)
                || world.Characters.Cold(slot) is not { } cold || !CanEdit(cold)) continue;
            if (edit.Mode == ColourMode.Uniform) { _uniform = edit.Palette; _hasUniform = true; }
            ColourSave.Set(cold, edit.Mode == ColourMode.Original ? null : new(edit.Mode, edit.Palette));
        }
    }

    // The native equipment/editor systems may replace Appearance earlier in this phase. Reapply only to a new look or changed palette.
    public void SlowWorld(World world) { _world = world; Refresh(world); }

    internal static bool CanEdit(CharacterCold cold) => cold.IsPlayer && cold.Medical?.Dead != true
        && cold.Appearance is { Animal: null };

    private void Refresh(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.IsAlive(table.IdOf(slot)) || table.Cold(slot) is not { } cold) continue;
            var assignment = ColourSave.Get(cold);
            if (assignment?.Mode == ColourMode.Uniform)
            {
                // Character fields are a fallback when an import or an external tool omits the world sidecar.
                if (!_hasUniform) { _uniform = assignment.Palette; _hasUniform = true; }
                if (assignment.Palette != _uniform)
                {
                    assignment = assignment with { Palette = _uniform };
                    ColourSave.Set(cold, assignment);
                }
            }
            if (assignment is null && !_applied.TryGetValue(cold, out _)) continue;
            var applied = _applied.GetValue(cold, _ => new Applied());
            if (assignment is null)
            {
                if (ReferenceEquals(cold.Appearance, applied.Tinted)) cold.Appearance = applied.Original;
                _applied.Remove(cold);
                continue;
            }
            if (cold.Appearance is not { Animal: null } look) continue;
            if (ReferenceEquals(look, applied.Tinted) && applied.Palette == assignment.Palette) continue;
            applied.Original = ColourAppearance.Original(look);
            applied.Tinted = ColourAppearance.Tint(applied.Original, assignment.Palette);
            applied.Palette = assignment.Palette;
            cold.Appearance = applied.Tinted;
        }
        RefreshView(world);
    }

    internal void RefreshView(World world)
    {
        var view = new Dictionary<CharacterId, ColourView>();
        foreach (var id in world.Player.Selection.Append(world.Player.Inspected).Distinct())
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold || !CanEdit(cold)) continue;
            var assignment = ColourSave.Get(cold);
            var natural = cold.Appearance!.Parts.Select(p => p.Colour).Concat(cold.Appearance.BodyLayers.Select(p => p.Colour))
                .FirstOrDefault(c => c is not null);
            var palette = assignment?.Palette ?? (natural is null ? ColourPalette.Default
                : new ColourPalette(natural.GetInt("color 1") & 0xFFFFFF, natural.GetInt("color 2") & 0xFFFFFF));
            view[id] = new(assignment?.Mode ?? ColourMode.Original, palette, _uniform);
        }
        Volatile.Write(ref _view, view);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_hasUniform);
        if (_hasUniform) { hasher.Add(_uniform.Primary); hasher.Add(_uniform.Secondary); }
        if (_world is null) return;
        for (int slot = 0; slot < _world.Characters.HighWater; slot++)
        {
            var id = _world.Characters.IdOf(slot);
            if (!_world.Characters.IsAlive(id) || _world.Characters.Cold(slot) is not { } cold || ColourSave.Get(cold) is not { } value) continue;
            hasher.Add(id); hasher.Add((int)value.Mode); hasher.Add(value.Palette.Primary); hasher.Add(value.Palette.Secondary);
        }
    }

    private sealed record SavedUniform(int Version, bool Active, ColourPalette Palette);
    public void Write(Stream stream) => JsonSerializer.Serialize(stream, new SavedUniform(1, _hasUniform, _uniform));
    public void Read(Stream stream)
    {
        var saved = JsonSerializer.Deserialize<SavedUniform>(stream);
        if (saved is not { Version: 1, Palette.Valid: true }) throw new InvalidDataException("Invalid CharTweak uniform save data.");
        _hasUniform = saved.Active; _uniform = saved.Palette;
    }
}
