using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MathKeyboard
{
    enum Act { Text, Back, Enter, Space, Left, Right, Shift, Tab }

    class Key
    {
        public string Label;
        public string Out;
        public Act Act = Act.Text;
        public int Page;
        public float X, Y, W = 1, H = 1;   // grid units
        public bool Repeat;
        public bool Greek;
    }

    class Ember
    {
        public float X, Y, VY, Sway, Phase, Size, Life, Max;
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

        // Types any text, independent of the active keyboard layout.
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
    }

    // The app logo: a sigma inside an ember flame on a dark rounded tile.
}
