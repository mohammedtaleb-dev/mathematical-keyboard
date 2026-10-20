using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MathKeyboard
{
    // Synthesised mechanical-keyboard sounds (no sample files). Each key makes a "down" sound
    // (sharp click + plastic body + low thock) and a softer, higher "up" sound on release; the
    // big keys (Space / Enter / Backspace) are deeper and add a small stabiliser rattle.
    static class Sfx
    {
        public const int Rate = 44100;
        public static short[][] Down, Up, BigDown, BigUp;

        static readonly Random rnd = new Random(11);

        // White noise through a 2-pole resonator, with an exponential decay envelope.
        static void AddNoiseBand(double[] buf, double t0, double fc, double bw, double tau, double gain, double dur)
        {
            int start = (int)(t0 * Rate), len = (int)(dur * Rate);
            double r = Math.Exp(-Math.PI * bw / Rate), th = 2 * Math.PI * fc / Rate;
            double a1 = 2 * r * Math.Cos(th), a2 = -r * r, norm = 1 - r;
            double y1 = 0, y2 = 0;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                double x = rnd.NextDouble() * 2 - 1;
                double y = a1 * y1 + a2 * y2 + norm * x;
                y2 = y1; y1 = y;
                double t = i / (double)Rate;
                buf[start + i] += gain * y * Math.Exp(-t / tau) * Math.Min(1.0, t / 0.0002);
            }
        }

        // Damped sine with a slight downward pitch glide: the "thock" of the case.
        static void AddThock(double[] buf, double t0, double f, double tau, double gain)
        {
            int start = (int)(t0 * Rate);
            double phase = 0;
            for (int n = start; n < buf.Length; n++)
            {
                double t = (n - start) / (double)Rate;
                double fi = f * (1.0 + 0.25 * Math.Exp(-t / 0.008));
                phase += 2 * Math.PI * fi / Rate;
                buf[n] += gain * Math.Min(1.0, t / 0.0004) * Math.Sin(phase) * Math.Exp(-t / tau);
            }
        }

        static short[] Finish(double[] buf, double peak)
        {
            double max = 0.0001;
            foreach (double d in buf) max = Math.Max(max, Math.Abs(d));
            double k = peak / max;
            short[] o = new short[buf.Length];
            int fade = Rate / 300;
            for (int i = 0; i < buf.Length; i++)
            {
                double v = buf[i] * k;
                if (i > buf.Length - fade) v *= (buf.Length - i) / (double)fade;
                o[i] = (short)(Math.Max(-1, Math.Min(1, v)) * 32000);
            }
            return o;
        }

        static double[] Buffer(double seconds) { return new double[(int)(seconds * Rate)]; }

        static short[] MakeDown(double p)
        {
            double[] b = Buffer(0.14);
            AddNoiseBand(b, 0.0000, 4200 * p, 3000, 0.0022, 1.00, 0.03);    // sharp click
            AddNoiseBand(b, 0.0004, 1700 * p, 900, 0.0090, 0.80, 0.07);     // plastic body
            AddThock(b, 0, 240 * p, 0.016, 0.90);
            AddThock(b, 0, 520 * p, 0.010, 0.35);
            return Finish(b, 0.50);
        }

        static short[] MakeUp(double p)
        {
            double[] b = Buffer(0.08);
            AddNoiseBand(b, 0, 5500 * p, 3500, 0.0012, 0.60, 0.02);
            AddNoiseBand(b, 0.0003, 2300 * p, 1200, 0.0045, 0.40, 0.04);
            AddThock(b, 0, 420 * p, 0.006, 0.30);
            return Finish(b, 0.26);
        }

        static short[] MakeBigDown(double p)
        {
            double[] b = Buffer(0.22);
            AddNoiseBand(b, 0.0000, 3000 * p, 2200, 0.0025, 0.90, 0.03);
            AddNoiseBand(b, 0.0005, 1100 * p, 700, 0.0140, 0.90, 0.10);
            AddThock(b, 0, 150 * p, 0.030, 1.10);
            AddThock(b, 0, 330 * p, 0.016, 0.40);
            AddNoiseBand(b, 0.0070, 5200 * p, 3000, 0.0012, 0.35, 0.01);     // stabiliser rattle
            AddNoiseBand(b, 0.0130, 4700 * p, 3000, 0.0012, 0.25, 0.01);
            return Finish(b, 0.55);
        }

        static short[] MakeBigUp(double p)
        {
            double[] b = Buffer(0.11);
            AddNoiseBand(b, 0, 4200 * p, 3000, 0.0014, 0.60, 0.02);
            AddNoiseBand(b, 0.0003, 1500 * p, 800, 0.0070, 0.45, 0.06);
            AddThock(b, 0, 220 * p, 0.012, 0.50);
            return Finish(b, 0.28);
        }

        public static void Init()
        {
            double[] pitch = { 0.93, 0.97, 1.00, 1.04, 1.08, 0.90 };
            Down = new short[pitch.Length][];
            Up = new short[pitch.Length][];
            BigDown = new short[pitch.Length][];
            BigUp = new short[pitch.Length][];
            for (int i = 0; i < pitch.Length; i++)
            {
                Down[i] = MakeDown(pitch[i]);
                Up[i] = MakeUp(pitch[i]);
                BigDown[i] = MakeBigDown(pitch[i]);
                BigUp[i] = MakeBigUp(pitch[i]);
            }
        }
    }

    // Small software mixer on top of the WinMM wave-out API, so several sounds can overlap when
    // typing fast. It sleeps (no CPU) whenever nothing is playing.
    static class Audio
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);

        const int Bufs = 3, Chunk = 441;   // 3 x 10 ms
        const uint CALLBACK_EVENT = 0x50000, WAVE_MAPPER = 0xFFFFFFFF;
        const uint WHDR_INQUEUE = 0x10;

        class Voice { public short[] Data; public int Pos; public float Gain; }

        static readonly List<Voice> voices = new List<Voice>();
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static readonly AutoResetEvent bufDone = new AutoResetEvent(false);
        static IntPtr dev;
        static IntPtr[] hdrs = new IntPtr[Bufs];
        static IntPtr[] data = new IntPtr[Bufs];
        static bool ready;
        static int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        static int flagsOffset = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");

        public static volatile bool Muted;
        public static int WriteErrors, Chunks;
        public static bool Ready { get { return ready; } }

        // Writes every effect, one after another, into a WAV file for listening / testing.
        public static void WritePreview(string path)
        {
            Sfx.Init();
            List<short> all = new List<short>();
            // a few normal key presses (down + up), then the big keys
            short[][][] seq =
            {
                new[] { Sfx.Down[2], Sfx.Up[2] }, new[] { Sfx.Down[4], Sfx.Up[4] }, new[] { Sfx.Down[0], Sfx.Up[0] },
                new[] { Sfx.Down[3], Sfx.Up[3] }, new[] { Sfx.BigDown[2], Sfx.BigUp[2] }, new[] { Sfx.BigDown[5], Sfx.BigUp[5] }
            };
            foreach (short[][] pair in seq)
            {
                all.AddRange(pair[0]);
                all.AddRange(new short[Sfx.Rate / 12]);   // finger-down time
                all.AddRange(pair[1]);
                all.AddRange(new short[Sfx.Rate / 4]);
            }
            using (System.IO.BinaryWriter w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int bytes = all.Count * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(Sfx.Rate); w.Write(Sfx.Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (short s in all) w.Write(s);
            }
        }

        public static void Init()
        {
            try
            {
                Sfx.Init();
                WAVEFORMATEX f = new WAVEFORMATEX();
                f.wFormatTag = 1; f.nChannels = 1; f.nSamplesPerSec = Sfx.Rate; f.wBitsPerSample = 16;
                f.nBlockAlign = 2; f.nAvgBytesPerSec = Sfx.Rate * 2;
                if (waveOutOpen(out dev, WAVE_MAPPER, ref f, bufDone.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT) != 0)
                    return;

                for (int i = 0; i < Bufs; i++)
                {
                    data[i] = Marshal.AllocHGlobal(Chunk * 2);
                    hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                    WAVEHDR h = new WAVEHDR();
                    h.lpData = data[i];
                    h.dwBufferLength = (uint)(Chunk * 2);
                    Marshal.StructureToPtr(h, hdrs[i], false);
                    if (waveOutPrepareHeader(dev, hdrs[i], hdrSize) != 0) return;
                }

                Thread t = new Thread(Run);
                t.IsBackground = true;
                t.Priority = ThreadPriority.AboveNormal;
                t.Start();
                ready = true;
            }
            catch { ready = false; }
        }

        public static void Play(short[] samples, float gain)
        {
            if (!ready || Muted || samples == null) return;
            lock (voices)
            {
                if (voices.Count > 12) voices.RemoveAt(0);
                voices.Add(new Voice { Data = samples, Gain = gain });
            }
            wake.Set();
        }

        static int FreeBuffer()
        {
            for (int i = 0; i < Bufs; i++)
                if (((uint)Marshal.ReadInt32(hdrs[i], flagsOffset) & WHDR_INQUEUE) == 0) return i;
            return -1;
        }

        static void Run()
        {
            int[] mix = new int[Chunk];
            short[] outBuf = new short[Chunk];
            while (true)
            {
                wake.WaitOne();
                while (true)
                {
                    lock (voices) { if (voices.Count == 0) break; }
                    int b = FreeBuffer();
                    if (b < 0) { bufDone.WaitOne(15); continue; }

                    Array.Clear(mix, 0, Chunk);
                    lock (voices)
                    {
                        for (int v = voices.Count - 1; v >= 0; v--)
                        {
                            Voice vo = voices[v];
                            int n = Math.Min(Chunk, vo.Data.Length - vo.Pos);
                            for (int i = 0; i < n; i++) mix[i] += (int)(vo.Data[vo.Pos + i] * vo.Gain);
                            vo.Pos += n;
                            if (vo.Pos >= vo.Data.Length) voices.RemoveAt(v);
                        }
                    }
                    for (int i = 0; i < Chunk; i++)
                        outBuf[i] = (short)Math.Max(-32768, Math.Min(32767, mix[i]));
                    Marshal.Copy(outBuf, 0, data[b], Chunk);
                    if (waveOutWrite(dev, hdrs[b], hdrSize) != 0) WriteErrors++;
                    Chunks++;
                }
            }
        }
    }
}
