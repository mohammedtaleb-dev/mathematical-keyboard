using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MathKeyboard
{
    enum Act { Text, Back, Enter, Space, Left, Right, Shift, Tab, Italic, Eq }

    class Key
    {
        public string Label;
        public string Out;
        public Act Act = Act.Text;
        public int Page;
        public float X, Y, W = 1, H = 1;   // grid units
        public bool Repeat;
        public bool Greek;   // page-2 key: Shift turns it into the upright capital
        public bool Math;    // single-letter variable: typed as math italic when Italic is on
        public string Eq;    // Word/PowerPoint/OneNote linear-format template, typed in Equation mode
    }

    class Ember
    {
        public float X, Y, VY, Sway, Phase, Size, Life, Max;
    }

    // Maps plain letters to the "Mathematical Italic" Unicode block so typed variables look like
    // equation text (x -> 𝑥, α -> 𝛼) in any app that has a math-capable fallback font.
    static class MathText
    {
        public static string Italic(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z')
                    sb.Append(c == 'h' ? "ℎ" : char.ConvertFromUtf32(0x1D44E + (c - 'a')));
                else if (c >= 'A' && c <= 'Z')
                    sb.Append(char.ConvertFromUtf32(0x1D434 + (c - 'A')));
                else if (c >= 'α' && c <= 'ω')
                    sb.Append(char.ConvertFromUtf32(0x1D6FC + (c - 'α')));
                else if (c == 'ϵ') sb.Append(char.ConvertFromUtf32(0x1D716));
                else if (c == 'ϑ') sb.Append(char.ConvertFromUtf32(0x1D717));
                else if (c == 'ϰ') sb.Append(char.ConvertFromUtf32(0x1D718));
                else if (c == 'ϕ') sb.Append(char.ConvertFromUtf32(0x1D719));
                else if (c == 'ϱ') sb.Append(char.ConvertFromUtf32(0x1D71A));
                else if (c == 'ϖ') sb.Append(char.ConvertFromUtf32(0x1D71B));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion U; }

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        static INPUT Make(ushort vk, ushort scan, uint flags)
        {
            INPUT i = new INPUT();
            i.type = INPUT_KEYBOARD;
            i.U.ki.wVk = vk;
            i.U.ki.wScan = scan;
            i.U.ki.dwFlags = flags;
            return i;
        }

        // Types any text (including surrogate pairs), independent of the active keyboard layout.
        public static void SendText(string s)
        {
            List<INPUT> list = new List<INPUT>();
            foreach (char c in s)
            {
                list.Add(Make(0, c, KEYEVENTF_UNICODE));
                list.Add(Make(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
            if (list.Count > 0) SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendVk(ushort vk, bool extended)
        {
            uint f = extended ? KEYEVENTF_EXTENDEDKEY : 0;
            INPUT[] a = { Make(vk, 0, f), Make(vk, 0, f | KEYEVENTF_KEYUP) };
            SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
        }

        // ---- translucent backdrop blur (Windows 10 accent policy) ----

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);

        public static void EnableBlur(IntPtr hwnd)
        {
            AccentPolicy policy = new AccentPolicy();
            policy.AccentState = 3;   // ACCENT_ENABLE_BLURBEHIND
            policy.AccentFlags = 2;
            policy.GradientColor = 0;
            int size = Marshal.SizeOf(policy);
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, p, false);
                WinCompAttrData d = new WinCompAttrData();
                d.Attribute = 19;   // WCA_ACCENT_POLICY
                d.Data = p;
                d.SizeOfData = size;
                SetWindowCompositionAttribute(hwnd, ref d);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        // ---- per-pixel alpha window surface ----

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPels, biYPels; public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION blend, uint flags);

        // A premultiplied-alpha DIB that GDI+ draws straight into and that is handed to the
        // layered window with UpdateLayeredWindow.
        public class Surface : IDisposable
        {
            public readonly int Width, Height;
            public readonly Bitmap Bmp;
            public readonly Graphics G;
            IntPtr memDc, hBitmap, oldObj;

            public Surface(int w, int h)
            {
                Width = w; Height = h;
                IntPtr screen = GetDC(IntPtr.Zero);
                memDc = CreateCompatibleDC(screen);
                BITMAPINFO bi = new BITMAPINFO();
                bi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bi.bmiHeader.biWidth = w;
                bi.bmiHeader.biHeight = -h;   // top-down
                bi.bmiHeader.biPlanes = 1;
                bi.bmiHeader.biBitCount = 32;
                IntPtr bits;
                hBitmap = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
                ReleaseDC(IntPtr.Zero, screen);
                oldObj = SelectObject(memDc, hBitmap);
                Bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                G = Graphics.FromImage(Bmp);
            }

            public void Present(IntPtr hwnd)
            {
                SIZE sz = new SIZE(); sz.cx = Width; sz.cy = Height;
                POINT src = new POINT();
                BLENDFUNCTION bf = new BLENDFUNCTION();
                bf.BlendOp = 0;            // AC_SRC_OVER
                bf.SourceConstantAlpha = 255;
                bf.AlphaFormat = 1;        // AC_SRC_ALPHA
                IntPtr screen = GetDC(IntPtr.Zero);
                UpdateLayeredWindow(hwnd, screen, IntPtr.Zero, ref sz, memDc, ref src, 0, ref bf, 2);
                ReleaseDC(IntPtr.Zero, screen);
            }

            public void Dispose()
            {
                G.Dispose();
                Bmp.Dispose();
                SelectObject(memDc, oldObj);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
            }
        }
    }
}
