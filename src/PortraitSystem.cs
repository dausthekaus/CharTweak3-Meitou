using System.Text;
using Meitou.Modding;
using Meitou.Simulation;

namespace CharTweak3;

internal sealed record SetPortrait(string Session, CharacterId Character, PortraitAsset? Asset) : SimCommand;
internal sealed record PortraitView(string Id, string Path, int Width, int Height);

/// <summary>Serial character assignments and an embedded image bundle, independently instantiated for every world.</summary>
internal sealed class PortraitSystem(PortraitFiles files) : ITickSystem, IWorldReady, IModSaveData, IStateHashed
{
    internal const int MaximumAssets = 128;
    private readonly Dictionary<string, PortraitAsset> _assets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);
    private Dictionary<CharacterId, PortraitView> _view = [];
    private World? _world;
    internal string Session { get; } = Guid.NewGuid().ToString("N");
    internal IReadOnlyDictionary<CharacterId, PortraitView> View => Volatile.Read(ref _view);
    internal static bool CanEdit(CharacterCold cold) => cold.IsPlayer && cold.Medical?.Dead != true && cold.Appearance is not null;

    public void Ready(World world, bool fromSave) { _world = world; Refresh(world); }
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _world = world;
        foreach (var command in commands)
        {
            if (command is not SetPortrait edit || edit.Session != Session
                || !world.Characters.TryResolveNext(edit.Character, out int slot)
                || world.Characters.Cold(slot) is not { } cold || !CanEdit(cold)) continue;
            if (edit.Asset is { } asset)
            {
                var used = Used(world).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
                if (!used.Contains(asset.Id) && used.Count >= MaximumAssets) continue;
                foreach (string unused in _assets.Keys.Where(id => !used.Contains(id)).ToArray()) _assets.Remove(unused);
                _assets[asset.Id] = asset;
            }
            PortraitSave.Set(cold, edit.Asset?.Id);
        }
    }
    public void SlowWorld(World world) { _world = world; Refresh(world); }

    private IEnumerable<(CharacterId Character, string Id)> Used(World world)
    {
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            var id = world.Characters.IdOf(slot);
            if (world.Characters.IsAlive(id) && world.Characters.Cold(slot) is { } cold && PortraitSave.Get(cold) is { } image)
                yield return (id, image);
        }
    }
    internal void Refresh(World world)
    {
        var view = new Dictionary<CharacterId, PortraitView>();
        foreach (var (character, id) in Used(world))
        {
            if (!_assets.TryGetValue(id, out var asset))
            {
                asset = _unavailable.Contains(id) ? null : files.Find(id);
                if (asset is null) { _unavailable.Add(id); view[character] = new(id, "", 1, 1); continue; }
                _assets[id] = asset;
            }
            view[character] = new(id, asset.Path, asset.Width, asset.Height);
        }
        Volatile.Write(ref _view, view);
    }
    public void Hash(ref StateHasher hasher)
    {
        if (_world is null) return;
        foreach (var (id, image) in Used(_world))
        { hasher.Add(id); foreach (char c in image) hasher.Add((int)c); }
    }
    public void Write(Stream stream)
    {
        var ids = _world is null ? [] : Used(_world).Select(p => p.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length > MaximumAssets) throw new InvalidDataException("Too many portrait assets.");
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        // Keep only assigned images, deduplicated across characters. Missing imports retain their native identity for cache recovery.
        var assets = ids.Select(id => _assets.GetValueOrDefault(id) ?? files.Find(id)).OfType<PortraitAsset>().ToArray();
        writer.Write(assets.Length);
        foreach (var asset in assets) { writer.Write(asset.Id); writer.Write(asset.Png.Length); writer.Write(asset.Png); }
    }
    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported portrait save version.");
        int count = reader.ReadInt32();
        if (count is < 0 or > MaximumAssets) throw new InvalidDataException("Invalid portrait asset count.");
        var restored = new Dictionary<string, PortraitAsset>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string id = reader.ReadString(); int length = reader.ReadInt32();
            if (length is <= 0 or > PortraitFiles.MaximumPngBytes || restored.ContainsKey(id))
                throw new InvalidDataException("Invalid or duplicate portrait asset.");
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new InvalidDataException("Portrait image was truncated.");
            restored.Add(id, PortraitFiles.Validate(id, bytes));
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected portrait save data.");
        foreach (var (id, asset) in restored) _assets[id] = files.Store(id, asset.Png);
    }
}
