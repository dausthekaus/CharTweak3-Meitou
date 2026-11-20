using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Meitou.Data.Textures;
using StbImageSharp;

namespace CharTweak3;

internal sealed record PortraitAsset(string Id, string Path, int Width, int Height, byte[] Png);

/// <summary>Content-addressed, portable PNG copies. No source filename or absolute path enters a save.</summary>
internal sealed class PortraitFiles(Action<string> log)
{
    internal const int MaximumSourceBytes = 16 * 1024 * 1024, MaximumPngBytes = 1100000, Size = 512;
    internal string DirectoryPath => Path.Combine(Path.GetDirectoryName(PaletteLibrary.DefaultPath())!, "portraits");
    internal static bool ValidId(string? id) => id is { Length: 64 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal PortraitAsset Import(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > MaximumSourceBytes) throw new InvalidDataException("Choose an image smaller than 16 MiB.");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
        using var input = new MemoryStream(bytes, writable: false);
        var info = ImageInfo.FromStream(input);
        if (info is null) throw new InvalidDataException("Could not decode the selected image.");
        if (info is not { Width: > 0 and <= 8192, Height: > 0 and <= 8192 } dimensions
            || (long)dimensions.Width * dimensions.Height > 16 * 1024 * 1024)
            throw new InvalidDataException("Image exceeds 8192px / 16 megapixels.");
        var image = TextureLoader.LoadImage(bytes);
        if (image.Width != dimensions.Width || image.Height != dimensions.Height) throw new InvalidDataException("Image dimensions changed during decoding.");
        var png = Encode(Fit(image));
        return Store(Convert.ToHexStringLower(SHA256.HashData(png)), png);
    }

    internal PortraitAsset? Find(string id)
    {
        if (!ValidId(id)) return null;
        try
        {
            string path = Path.Combine(DirectoryPath, id + ".png");
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaximumPngBytes) throw new InvalidDataException("Portrait cache image is too large.");
            return Validate(id, File.ReadAllBytes(path), path);
        }
        catch (Exception e)
        { log($"Portrait {id} could not be read: {e.Message}"); return null; }
    }

    // Also validates embedded save assets before accepting them. Store keeps the bytes even if a read-only cache prevents display.
    internal PortraitAsset Store(string id, byte[] png)
    {
        var asset = Validate(id, png, Path.Combine(DirectoryPath, id + ".png"));
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            temporary = asset.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, asset.Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { log($"Portrait cache could not be written; image retained in save data: {e.Message}"); }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
        return asset;
    }

    internal static PortraitAsset Validate(string id, byte[] png, string path = "")
    {
        if (!ValidId(id) || png.Length is < 33 or > MaximumPngBytes
            || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            || Convert.ToHexStringLower(SHA256.HashData(png)) != id)
            throw new InvalidDataException("Invalid portrait image or content hash.");
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        if (width is <= 0 or > Size || height is <= 0 or > Size || png[24] != 8 || png[25] != 6)
            throw new InvalidDataException("Invalid normalized portrait dimensions or format.");
        // Decode once before committing: a valid header/hash alone does not prove the image can render.
        var decoded = TextureLoader.LoadImage(png);
        if (decoded.Width != width || decoded.Height != height) throw new InvalidDataException("Invalid portrait pixels.");
        return new(id, path, width, height, png);
    }

    private static RgbaImage Fit(RgbaImage image)
    {
        float scale = Math.Min(1, (float)Size / Math.Max(image.Width, image.Height));
        int width = Math.Max(1, (int)MathF.Round(image.Width * scale)), height = Math.Max(1, (int)MathF.Round(image.Height * scale));
        if (width == image.Width && height == image.Height) return image;
        var result = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sx = Math.Clamp((x + .5f) * image.Width / width - .5f, 0, image.Width - 1);
            float sy = Math.Clamp((y + .5f) * image.Height / height - .5f, 0, image.Height - 1);
            int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
            float dx = sx - x0, dy = sy - y0;
            for (int channel = 0; channel < 4; channel++)
            {
                float Sample(int px, int py) => image.Pixels[(py * image.Width + px) * 4 + channel];
                float top = Sample(x0, y0) * (1 - dx) + Sample(x1, y0) * dx;
                float bottom = Sample(x0, y1) * (1 - dx) + Sample(x1, y1) * dx;
                result.Pixels[(y * width + x) * 4 + channel] = (byte)Math.Clamp((int)MathF.Round(top * (1 - dy) + bottom * dy), 0, 255);
            }
        }
        return result;
    }

    /// <summary>Small PNG encoder using BCL zlib; 8-bit RGBA, no metadata, top-to-bottom rows.</summary>
    private static byte[] Encode(RgbaImage image)
    {
        using var output = new MemoryStream(); output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height); header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < image.Height; y++)
            { zlib.WriteByte(0); zlib.Write(image.Pixels, y * image.Width * 4, image.Width * 4); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        byte[] name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        uint crc = 0xffffffff;
        foreach (byte b in name.Concat(data))
        { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
