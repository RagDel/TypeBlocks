using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace TypeBlocks;

internal static class AppFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<float, Font> Fonts = new();
    // Both GDI (native edit controls/TextRenderer) and GDI+ use this process-private font.
    // The collection, backing memory and registration live for the application lifetime.
    private static readonly IntPtr FontMemory;
    private static readonly IntPtr GdiRegistration;

    static AppFonts()
    {
        using var resource = typeof(AppFonts).Assembly.GetManifestResourceStream("TypeBlocks.Assets.Geologica-Light.ttf")
            ?? throw new InvalidOperationException("The bundled Geologica font is missing.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        byte[] data = bytes.ToArray();
        FontMemory = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, FontMemory, data.Length);
        Collection.AddMemoryFont(FontMemory, data.Length);
        uint count = 0;
        GdiRegistration = AddFontMemResourceEx(FontMemory, (uint)data.Length, IntPtr.Zero, ref count);
        if (GdiRegistration == IntPtr.Zero || count == 0 || Collection.Families.Length == 0)
            throw new InvalidOperationException("The bundled Geologica font could not be loaded.");
    }

    public static Font Get(float size)
    {
        if (!Fonts.TryGetValue(size, out var font))
            Fonts[size] = font = new Font(Collection.Families[0], size, FontStyle.Regular, GraphicsUnit.Point);
        return font;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);
}
