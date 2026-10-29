using System.Drawing.Imaging;

namespace TypeBlocks;

internal static class AppIconSmokeTests
{
    public static void Verify(string outputDirectory)
    {
        using var source = typeof(AppIcon).Assembly.GetManifestResourceStream(AppIcon.ResourceName)
            ?? throw new Exception("The application icon was not embedded.");
        using var reader = new BinaryReader(source);
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != sizes.Length)
            throw new Exception("The application icon directory is invalid.");
        var frames = new List<(int Size, uint Length, uint Offset)>();
        foreach (int size in sizes)
        {
            int width = reader.ReadByte(), height = reader.ReadByte();
            if ((width == 0 ? 256 : width) != size || (height == 0 ? 256 : height) != size)
                throw new Exception("An application icon size is missing.");
            reader.ReadByte(); reader.ReadByte();
            if (reader.ReadUInt16() != 1 || reader.ReadUInt16() != 32)
                throw new Exception("An application icon frame has the wrong color depth.");
            frames.Add((size, reader.ReadUInt32(), reader.ReadUInt32()));
        }
        foreach (var frame in frames)
        {
            source.Position = frame.Offset;
            using var encoded = new MemoryStream(reader.ReadBytes(checked((int)frame.Length)));
            using var bitmap = new Bitmap(encoded);
            if (bitmap.Width != frame.Size || bitmap.Height != frame.Size || bitmap.GetPixel(0, 0).A != 0)
                throw new Exception("An application icon frame lost its dimensions or transparency.");
            if (frame.Size != 256) continue;
            using var preview = new Bitmap(544, 284);
            using var graphics = Graphics.FromImage(preview);
            graphics.Clear(Color.FromArgb(247, 248, 250));
            using var dark = new SolidBrush(Color.FromArgb(31, 35, 41));
            graphics.FillRectangle(dark, 272, 0, 272, 284);
            graphics.DrawImageUnscaled(bitmap, 8, 14);
            graphics.DrawImageUnscaled(bitmap, 280, 14);
            preview.Save(Path.Combine(outputDirectory, "app-icon.png"), ImageFormat.Png);
        }
        if (AppIcon.Application.Handle == IntPtr.Zero)
            throw new Exception("The shared application icon did not load.");
        using var form = new Form();
        Ui.Style(form, "Icon verification", new Size(200, 100));
        if (!ReferenceEquals(form.Icon, AppIcon.Application))
            throw new Exception("Forms do not use the shared application icon.");
    }
}
