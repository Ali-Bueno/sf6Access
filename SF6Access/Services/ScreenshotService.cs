using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;

namespace SF6Access.Services;

/// <summary>
/// F7: save a PNG screenshot of the game window to reframework/data, one new
/// timestamped file per press, so a tester can capture several menu states in a
/// single session as a visual reference alongside the F9 dumps.
///
/// Capture uses GDI (BitBlt from the screen DC over the foreground window's
/// rect), which works for borderless/windowed games composited by the desktop.
/// The PNG is encoded by hand via DeflateStream so the mod needs no extra image
/// library — and PNG (not BMP) so the captures can be read back as images.
/// </summary>
public static class ScreenshotService
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    private const int VK_F7 = 0x76;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint DIB_RGB_COLORS = 0;

    private static bool _lastKey;
    private static int _count;

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    public static void OnUpdate()
    {
        bool down = (GetAsyncKeyState(VK_F7) & 0x8000) != 0;
        if (down && !_lastKey)
        {
            try
            {
                string path = Capture();
                _count++;
                ScreenReaderService.Speak($"Screenshot {_count} saved");
                API.LogInfo($"[SF6Access] Screenshot saved to {path}");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Screenshot failed: {ex.Message}");
                ScreenReaderService.Speak("Screenshot failed");
            }
        }
        _lastKey = down;
    }

    /// <summary>Grab the foreground window (or the primary screen) and write a PNG.</summary>
    private static string Capture()
    {
        int x = 0, y = 0, w, h;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r) &&
            r.Right > r.Left && r.Bottom > r.Top)
        {
            x = r.Left; y = r.Top;
            w = r.Right - r.Left; h = r.Bottom - r.Top;
        }
        else
        {
            w = GetSystemMetrics(SM_CXSCREEN);
            h = GetSystemMetrics(SM_CYSCREEN);
        }

        byte[] bgr = GrabPixels(x, y, w, h, out int stride);
        byte[] png = EncodePng(bgr, w, h, stride);

        string name = $"sf6access_shot_{DateTime.Now:HHmmss}_{_count}.png";
        string path = Path.Combine(ObjectDumper.DumpDir, name);
        File.WriteAllBytes(path, png);
        return path;
    }

    /// <summary>BitBlt the screen region into a DIB and return 24-bit BGR rows (bottom-up).</summary>
    private static byte[] GrabPixels(int x, int y, int w, int h, out int stride)
    {
        stride = ((w * 3 + 3) / 4) * 4; // each BMP/DIB row is padded to 4 bytes
        IntPtr screenDC = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(screenDC);
        IntPtr bmp = CreateCompatibleBitmap(screenDC, w, h);
        IntPtr old = SelectObject(memDC, bmp);
        try
        {
            BitBlt(memDC, 0, 0, w, h, screenDC, x, y, SRCCOPY);
            SelectObject(memDC, old); // deselect before GetDIBits

            var bi = new BITMAPINFOHEADER
            {
                biSize = 40,
                biWidth = w,
                biHeight = h, // positive → rows returned bottom-up (BMP order)
                biPlanes = 1,
                biBitCount = 24,
                biCompression = 0, // BI_RGB
            };
            byte[] buffer = new byte[stride * h];
            GetDIBits(memDC, bmp, 0, (uint)h, buffer, ref bi, DIB_RGB_COLORS);
            return buffer;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    /// <summary>Encode bottom-up BGR rows as a truecolor PNG (no extra dependencies).</summary>
    private static byte[] EncodePng(byte[] bgr, int w, int h, int stride)
    {
        // Top-down RGB scanlines, each prefixed with filter byte 0 (none).
        byte[] raw = new byte[(w * 3 + 1) * h];
        int p = 0;
        for (int row = 0; row < h; row++)
        {
            raw[p++] = 0; // filter: none
            int src = (h - 1 - row) * stride; // flip vertical (DIB is bottom-up)
            for (int col = 0; col < w; col++)
            {
                byte b = bgr[src++], g = bgr[src++], rr = bgr[src++];
                raw[p++] = rr; raw[p++] = g; raw[p++] = b; // BGR → RGB
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        // IHDR
        byte[] ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w);
        WriteBE(ihdr, 4, (uint)h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type: truecolor RGB
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT: zlib stream = header + raw DEFLATE + Adler-32 of the raw data
        using var idat = new MemoryStream();
        idat.WriteByte(0x78);
        idat.WriteByte(0x9C);
        using (var deflate = new DeflateStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);
        byte[] adler = new byte[4];
        WriteBE(adler, 0, Adler32(raw));
        idat.Write(adler, 0, 4);
        WriteChunk(ms, "IDAT", idat.ToArray());

        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteBE(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v >> 24); buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8); buf[off + 3] = (byte)v;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        byte[] len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);

        byte[] typeBytes = { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);

        uint crc = Crc32(typeBytes, data);
        byte[] crcBuf = new byte[4];
        WriteBE(crcBuf, 0, crc);
        s.Write(crcBuf, 0, 4);
    }

    private static uint Adler32(byte[] data)
    {
        const uint MOD = 65521;
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % MOD;
            b = (b + a) % MOD;
        }
        return (b << 16) | a;
    }

    private static uint[] _crcTable;
    private static uint Crc32(byte[] type, byte[] data)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                _crcTable[n] = c;
            }
        }
        uint crc = 0xFFFFFFFF;
        foreach (byte t in type) crc = _crcTable[(crc ^ t) & 0xFF] ^ (crc >> 8);
        foreach (byte d in data) crc = _crcTable[(crc ^ d) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
