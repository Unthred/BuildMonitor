using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GenerateTrayIcons;

internal static class IconFileWriter
{
    public static void WriteMultiSizeIcon(string path, IReadOnlyList<Bitmap> bitmaps)
    {
        var pngImages = new byte[bitmaps.Count][];
        for (var i = 0; i < bitmaps.Count; i++)
        {
            using var pngStream = new MemoryStream();
            bitmaps[i].Save(pngStream, ImageFormat.Png);
            pngImages[i] = pngStream.ToArray();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngImages.Length);

        var offset = 6 + 16 * pngImages.Length;
        for (var i = 0; i < bitmaps.Count; i++)
        {
            WriteDirectoryEntry(writer, bitmaps[i].Width, bitmaps[i].Height, pngImages[i].Length, offset);
            offset += pngImages[i].Length;
        }

        foreach (var png in pngImages)
        {
            writer.Write(png);
        }
    }

    private static void WriteDirectoryEntry(BinaryWriter writer, int width, int height, int dataSize, int offset)
    {
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(dataSize);
        writer.Write(offset);
    }
}
