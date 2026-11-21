using Meitou.Data.Textures;

namespace CharTweak3;

/// <summary>Top-down still photo, composited over a centered square crop of the background.</summary>
internal static class PhotoImage
{
    internal static RgbaImage Compose(RgbaImage character, RgbaImage? background)
    {
        var result = new RgbaImage(character.Width, character.Height);
        int side = background is null ? 1 : Math.Min(background.Width, background.Height);
        int left = background is null ? 0 : (background.Width - side) / 2;
        int top = background is null ? 0 : (background.Height - side) / 2;
        for (int y = 0; y < character.Height; y++)
        for (int x = 0; x < character.Width; x++)
        {
            int i = (y * character.Width + x) * 4;
            int bx = left + Math.Min(side - 1, x * side / character.Width);
            int by = top + Math.Min(side - 1, y * side / character.Height);
            int b = background is null ? 0 : (by * background.Width + bx) * 4;
            int alpha = character.Pixels[i + 3];
            for (int c = 0; c < 3; c++)
            {
                int behind = background is null ? 48 : (background.Pixels[b + c] * background.Pixels[b + 3] + 48 * (255 - background.Pixels[b + 3]) + 127) / 255;
                result.Pixels[i + c] = (byte)((character.Pixels[i + c] * alpha + behind * (255 - alpha) + 127) / 255);
            }
            result.Pixels[i + 3] = 255;
        }
        return result;
    }

    internal static RgbaImage FromReadback(byte[] pixels, int size)
    {
        if (size <= 0 || size > PortraitFiles.Size || pixels.Length != size * size * 4)
            throw new InvalidDataException("Invalid photo readback.");
        var image = new RgbaImage(size, size);
        for (int y = 0; y < size; y++)
            pixels.AsSpan((size - 1 - y) * size * 4, size * 4).CopyTo(image.Pixels.AsSpan(y * size * 4));
        if (!Enumerable.Range(0, size * size).Any(i => image.Pixels[i * 4 + 3] > 0))
            throw new InvalidDataException("The character is not ready in the preview. Wait or move the game camera nearer.");
        return image;
    }
}
