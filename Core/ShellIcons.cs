using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Perch.Core;

/// <summary>Large, alpha-correct icons for files, shortcuts, packaged apps and windows.</summary>
public static class ShellIcons
{
    static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    const int Size = 96;

    /// <summary>Icon for a path, shortcut, or "shell:AppsFolder\..." moniker. Cached; may return null.</summary>
    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd(path, Load);
    }

    static ImageSource? Load(string path)
    {
        try
        {
            string parse = path;
            if (!parse.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !File.Exists(parse) && !Directory.Exists(parse)) return null;

            Native.SHCreateItemFromParsingName(parse, IntPtr.Zero, typeof(Native.IShellItemImageFactory).GUID, out var factory);
            try
            {
                int hr = factory.GetImage(new Native.SIZE { cx = Size, cy = Size }, Native.SIIGBF_ICONONLY | Native.SIIGBF_BIGGERSIZEOK, out var hbmp);
                if (hr != 0 || hbmp == IntPtr.Zero) return null;
                try { return FromHBitmap(hbmp); }
                finally { Native.DeleteObject(hbmp); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch { return null; }
    }

    /// <summary>Copies a 32-bit DIB section keeping its alpha channel (Imaging.CreateBitmapSourceFromHBitmap drops it).</summary>
    static BitmapSource? FromHBitmap(IntPtr hbmp)
    {
        if (Native.GetObject(hbmp, Marshal.SizeOf<Native.DIBSECTION>(), out var ds) == 0) return null;
        var bm = ds.dsBm;
        if (bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32)
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight), stride = bm.bmWidthBytes;
        var pixels = new byte[stride * h];
        Marshal.Copy(bm.bmBits, pixels, 0, pixels.Length);

        bool bottomUp = ds.dsBmih.biHeight > 0;
        if (bottomUp)
        {
            var flipped = new byte[pixels.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);
            pixels = flipped;
        }
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        // The shell hands back either premultiplied or straight alpha depending on the icon.
        // In premultiplied data no colour channel can exceed alpha; if one does, the data is straight.
        // Treating straight alpha as premultiplied makes soft edges too bright: a pale halo round the icon.
        bool straight = false;
        for (int i = 0; i < pixels.Length && !straight; i += 4)
        {
            byte a = pixels[i + 3];
            if (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a) straight = true;
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The icon a window reports for itself (fallback for windows whose app icon can't be found).</summary>
    public static ImageSource? FromWindow(IntPtr hwnd)
    {
        try
        {
            Native.SendMessageTimeout(hwnd, Native.WM_GETICON, new IntPtr(1), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out var icon); // ICON_BIG
            if (icon == IntPtr.Zero) icon = Native.GetClassLongPtr(hwnd, Native.GCLP_HICON);
            if (icon == IntPtr.Zero) Native.SendMessageTimeout(hwnd, Native.WM_GETICON, new IntPtr(2), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out icon);
            if (icon == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    public static ImageSource? RecycleBin(bool full)
    {
        string key = full ? "::recycler-full" : "::recycler";
        return Cache.GetOrAdd(key, _ =>
        {
            var info = new Native.SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<Native.SHSTOCKICONINFO>() };
            if (Native.SHGetStockIconInfo(full ? Native.SIID_RECYCLERFULL : Native.SIID_RECYCLER, Native.SHGSI_ICONLOCATION, ref info) != 0) return null;
            // The stock icon is a resource in a DLL; the Recycle Bin folder itself renders the right state at high resolution.
            return Load("shell:::{645FF040-5081-101B-9F08-00AA002F954E}");
        });
    }

    public static void Forget(string key) => Cache.TryRemove(key, out _);
}
