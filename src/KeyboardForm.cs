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
    class KeyboardForm : Form
    {
        const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008;

        static readonly Color BgColor = Color.FromArgb(0x1A, 0x16, 0x15);
        static readonly Color TitleColor = Color.FromArgb(0x12, 0x0E, 0x0D);
        static readonly Color KeyColor = Color.FromArgb(0x35, 0x30, 0x2F);
        static readonly Color HoverColor = Color.FromArgb(0x52, 0x40, 0x38);
        static readonly Color PressColor = Color.FromArgb(0xE0, 0x62, 0x1E);
        static readonly Color AccentA = Color.FromArgb(0xE8, 0x6A, 0x17);
        static readonly Color AccentB = Color.FromArgb(0xB8, 0x2A, 0x10);
        static readonly Color TextColor = Color.FromArgb(0xF4, 0xEE, 0xEA);

        const string MathFont = "Cambria Math";
        const string UiFont = "Segoe UI";
        const float NumpadX = 10.4f;
        const float TotalW = 14.4f;
        const int Rows = 6;
        const int Cols = 10;
        const int PerPage = 40;
        const float AnimDur = 0.55f;

        readonly string[] tabNames =
        {
            "∫  Calculus", "∑  Algebra", "π  Greek",
            "sin  Trig", "≤  Logic", "x²  Powers"
        };

        readonly List<Key[]> pages = new List<Key[]>();
        readonly List<Key> keys = new List<Key>();
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        readonly Dictionary<string, Font> fitCache = new Dictionary<string, Font>();
        readonly Timer repeatTimer = new Timer();
        readonly Timer frameTimer = new Timer();
        readonly List<Ember> embers = new List<Ember>();
        readonly Random rng = new Random();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Bitmap titleLogo;

        Bitmap cache;
        bool dirty = true;

        int page;
        bool shift;
        Key hover, down;
        int hoverTitle;   // 1 = minimize, 2 = close
        float scale = 1f;

        // animation state
        float animT = AnimDur;
        int animDir = 1;
        float hlX;                 // animated tab highlight position (grid units)
        double lastTime;

        int TitleH { get { return (int)(28 * scale); } }
        int Pad { get { return (int)(8 * scale); } }
        int Gap { get { return Math.Max(2, (int)(2 * scale)); } }

        public KeyboardForm()
        {
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

            Text = "Mathematical Keyboard";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            BackColor = BgColor;
            ClientSize = new Size((int)(860 * scale), (int)(290 * scale));
            MinimumSize = new Size((int)(600 * scale), (int)(220 * scale));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(16 * scale));

            using (Bitmap small = Logo.Render(32))
                Icon = Icon.FromHandle(small.GetHicon());
            titleLogo = Logo.Render((int)(40 * scale));

            BuildPages();
            Build();
            SpawnInitialEmbers();

            repeatTimer.Tick += delegate
            {
                repeatTimer.Interval = 45;
                if (down != null) Press(down);
            };

            lastTime = clock.Elapsed.TotalSeconds;
            frameTimer.Interval = 33;
            frameTimer.Tick += delegate { Frame(); };
            frameTimer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        // ---------- symbol pages ----------

        static void S(List<Key> l, string chars)
        {
            foreach (char c in chars)
                if (c != ' ') l.Add(new Key { Label = c.ToString(), Out = c.ToString() });
        }

        static void T(List<Key> l, string label, string output)
        {
            l.Add(new Key { Label = label, Out = output });
        }

        void BuildPages()
        {
            List<Key> p = new List<Key>();

            // 0: calculus
            S(p, "∫ ∬ ∭ ∮ ∯ ∂ ∇ ∞ ′ ″");
            T(p, "lim", "lim");
            T(p, "x→∞", "x→∞");
            T(p, "x→0", "x→0");
            T(p, "−∞", "−∞");
            T(p, "dx", "dx"); T(p, "dy", "dy"); T(p, "dz", "dz"); T(p, "dt", "dt");
            T(p, "dθ", "dθ");
            T(p, "d/dx", "d/dx"); T(p, "dy/dx", "dy/dx"); T(p, "∂/∂x", "∂/∂x");
            T(p, "f(x)", "f(x)"); T(p, "f′(x)", "f′(x)");
            S(p, "Δ δ ε → ∑ √ ∝ ±");
            pages.Add(p.ToArray());

            // 1: algebra
            p = new List<Key>();
            S(p, "∑ ∏ ∐ √ ∛ ∜ ! ^ _ /");
            S(p, "± ∓ × ÷ · ∘ % ° ∠ ⟂");
            S(p, "∥ △ ⌊ ⌋ ⌈ ⌉ ‖ | ( )");
            S(p, "[ ] { } < > x y z n");
            pages.Add(p.ToArray());

            // 2: greek
            p = new List<Key>();
            S(p, "α β γ δ ε ζ η θ ι κ");
            S(p, "λ μ ν ξ ο π ρ σ τ υ");
            S(p, "φ χ ψ ω ϕ ϑ ϱ ϖ ς ϵ");
            foreach (Key k in p) k.Greek = true;
            pages.Add(p.ToArray());

            // 3: trig
            p = new List<Key>();
            foreach (string f in new[] { "sin", "cos", "tan", "cot", "sec", "csc" }) T(p, f, f + "(");
            foreach (string f in new[] { "sin", "cos", "tan" }) T(p, f + "⁻¹", f + "⁻¹(");
            T(p, "π", "π");
            foreach (string f in new[] { "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh" }) T(p, f, f + "(");
            T(p, "θ", "θ"); T(p, "°", "°"); T(p, "rad", "rad"); T(p, "e", "e");
            foreach (string f in new[] { "log", "ln", "exp", "abs" }) T(p, f, f + "(");
            T(p, "eˣ", "e^"); T(p, "√", "√"); T(p, "(", "("); T(p, ")", ")");
            pages.Add(p.ToArray());

            // 4: relations / logic / sets
            p = new List<Key>();
            S(p, "≤ ≥ ≠ ≈ ≡ ≅ ∼ < > =");
            S(p, "∈ ∉ ∋ ⊂ ⊃ ⊆ ⊇ ∪ ∩ ∅");
            S(p, "∖ ∀ ∃ ∄ ¬ ∧ ∨ ⇒ ⇔ →");
            S(p, "← ↔ ↦ ∴ ∵ ℕ ℤ ℚ ℝ ℂ");
            pages.Add(p.ToArray());

            // 5: superscripts / subscripts
            p = new List<Key>();
            S(p, "⁰ ¹ ² ³ ⁴ ⁵ ⁶ ⁷ ⁸ ⁹");
            S(p, "⁺ ⁻ ⁼ ⁽ ⁾ ⁿ ⁱ ˣ ʸ ᶻ");
            S(p, "₀ ₁ ₂ ₃ ₄ ₅ ₆ ₇ ₈ ₉");
            S(p, "₊ ₋ ₌ ₍ ₎ ₙ ₓ ᵢ ⱼ ₐ");
            pages.Add(p.ToArray());
        }

        // Rebuilds the visible key list: tabs, current page, control row and the numpad.
        void Build()
        {
            keys.Clear();

            float tw = (float)Cols / tabNames.Length;
            for (int i = 0; i < tabNames.Length; i++)
                keys.Add(new Key { Label = tabNames[i], Act = Act.Tab, Page = i, X = i * tw, Y = 0, W = tw });

            Key[] items = pages[page];
            for (int i = 0; i < items.Length && i < PerPage; i++)
            {
                items[i].X = i % Cols;
                items[i].Y = 1 + i / Cols;
                keys.Add(items[i]);
            }

            keys.Add(new Key { Label = "⇧ Shift", Act = Act.Shift, X = 0, Y = 5, W = 2 });
            keys.Add(new Key { Label = "Space", Act = Act.Space, X = 2, Y = 5, W = 5, Repeat = true });
            keys.Add(new Key { Label = "←", Act = Act.Left, X = 7, Y = 5, W = 1.5f, Repeat = true });
            keys.Add(new Key { Label = "→", Act = Act.Right, X = 8.5f, Y = 5, W = 1.5f, Repeat = true });

            keys.Add(new Key { Label = "⌫", Act = Act.Back, X = NumpadX, Y = 0, W = 2, Repeat = true });
            keys.Add(new Key { Label = "↵", Act = Act.Enter, X = NumpadX + 2, Y = 0, W = 2 });

            string[][] pad =
            {
                new[] { "7", "8", "9", "÷" },
                new[] { "4", "5", "6", "×" },
                new[] { "1", "2", "3", "-" },
                new[] { "0", ".", "=", "+" },
                new[] { "(", ")", ",", "^" },
            };
            for (int r = 0; r < pad.Length; r++)
                for (int c = 0; c < 4; c++)
                    keys.Add(new Key { Label = pad[r][c], Out = pad[r][c], X = NumpadX + c, Y = 1 + r });
        }

        // ---------- geometry ----------

        float UnitX { get { return (ClientSize.Width - 2f * Pad) / TotalW; } }
        float UnitY { get { return (ClientSize.Height - TitleH - 2f * Pad) / Rows; } }

        Rectangle KeyRect(Key k)
        {
            return new Rectangle(
                (int)Math.Round(Pad + k.X * UnitX), (int)Math.Round(TitleH + Pad + k.Y * UnitY),
                (int)Math.Round(k.W * UnitX), (int)Math.Round(k.H * UnitY));
        }

        Rectangle MinRect { get { return new Rectangle(ClientSize.Width - TitleH * 3, 0, TitleH * 3 / 2, TitleH); } }
        Rectangle CloseRect { get { return new Rectangle(ClientSize.Width - TitleH * 3 / 2, 0, TitleH * 3 / 2, TitleH); } }

        Key HitTest(Point p)
        {
            foreach (Key k in keys)
                if (KeyRect(k).Contains(p)) return k;
            return null;
        }

        // ---------- behaviour ----------

        void SetPage(int newPage)
        {
            if (newPage == page) return;
            animDir = newPage > page ? 1 : -1;
            page = newPage;
            animT = 0;
            Build();
        }

        void Press(Key k)
        {
            switch (k.Act)
            {
                case Act.Text:
                    string s = k.Out;
                    if (k.Greek && shift) s = s.ToUpperInvariant();
                    Native.SendText(s);
                    break;
                case Act.Back: Native.SendVk(0x08, false); break;
                case Act.Enter: Native.SendVk(0x0D, false); break;
                case Act.Space: Native.SendVk(0x20, false); break;
                case Act.Left: Native.SendVk(0x25, true); break;
                case Act.Right: Native.SendVk(0x27, true); break;
                case Act.Shift: shift = !shift; break;
                case Act.Tab:
                    down = null;
                    SetPage(k.Page);
                    break;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (CloseRect.Contains(e.Location)) { Close(); return; }
            if (MinRect.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }

            Key k = HitTest(e.Location);
            if (k == null) return;
            down = k;
            dirty = true;
            Press(k);
            if (down != null && k.Repeat)
            {
                repeatTimer.Interval = 400;
                repeatTimer.Start();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            repeatTimer.Stop();
            down = null;
            dirty = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Key h = HitTest(e.Location);
            int t = CloseRect.Contains(e.Location) ? 2 : MinRect.Contains(e.Location) ? 1 : 0;
            if (h != hover || t != hoverTitle) dirty = true;
            hover = h;
            hoverTitle = t;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            hoverTitle = 0;
            dirty = true;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84, WM_NCLBUTTONDBLCLK = 0xA3;
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)3; return; }   // MA_NOACTIVATE
            if (m.Msg == WM_NCLBUTTONDBLCLK) return;
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int grip = (int)(18 * scale);
                if (p.X >= ClientSize.Width - grip && p.Y >= ClientSize.Height - grip) { m.Result = (IntPtr)17; return; }
                if (p.Y >= 0 && p.Y < TitleH && p.X < MinRect.Left) { m.Result = (IntPtr)2; return; }
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- animation + embers ----------

        float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        void Respawn(Ember e, bool anywhere)
        {
            e.X = Rand(0, ClientSize.Width);
            e.Y = anywhere ? Rand(ClientSize.Height * 0.35f, ClientSize.Height) : ClientSize.Height - Rand(0, 8 * scale);
            e.VY = Rand(14, 42) * scale;
            e.Sway = Rand(8, 22) * scale;
            e.Phase = Rand(0, 6.28f);
            e.Size = Rand(1.1f, 2.9f) * scale;
            e.Max = Rand(2.4f, 5.2f);
            e.Life = anywhere ? Rand(0.2f, 1f) * e.Max : e.Max;
        }

        void SpawnInitialEmbers()
        {
            for (int i = 0; i < 38; i++)
            {
                Ember e = new Ember();
                Respawn(e, true);
                embers.Add(e);
            }
        }

        void Frame()
        {
            if (WindowState == FormWindowState.Minimized) return;
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, now - lastTime);
            lastTime = now;
            Advance(dt);
            Invalidate();
        }

        void Advance(float dt)
        {
            if (animT < AnimDur)
            {
                animT = Math.Min(AnimDur, animT + dt);
                dirty = true;
            }

            float target = page * ((float)Cols / tabNames.Length);
            if (hlX != target)
            {
                hlX += (target - hlX) * Math.Min(1f, dt * 13f);
                if (Math.Abs(target - hlX) < 0.002f) hlX = target;
                dirty = true;
            }

            float t = (float)clock.Elapsed.TotalSeconds;
            foreach (Ember e in embers)
            {
                e.Life -= dt;
                e.Y -= e.VY * dt;
                e.X += (float)Math.Sin(t * 1.7f + e.Phase) * e.Sway * dt;
                if (e.Life <= 0 || e.Y < TitleH) Respawn(e, false);
            }
        }

        // ---------- painting ----------

        Font GetFont(string family, int px)
        {
            string id = family + px;
            Font f;
            if (!fonts.TryGetValue(id, out f))
            {
                f = new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
                fonts[id] = f;
            }
            return f;
        }

        const TextFormatFlags Flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        static bool IsAscii(string s)
        {
            foreach (char c in s) if (c > 127) return false;
            return true;
        }

        Font FitFont(Graphics g, string text, string family, int maxPx, int width)
        {
            string id = family + "|" + maxPx + "|" + width + "|" + text;
            Font f;
            if (fitCache.TryGetValue(id, out f)) return f;
            int px = maxPx;
            f = GetFont(family, px);
            while (px > 9 && TextRenderer.MeasureText(g, text, f, new Size(10000, 200), Flags).Width > width - 8)
                f = GetFont(family, --px);
            fitCache[id] = f;
            return f;
        }

        static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        // Warm glow rising from the bottom edge. Baked into the key layer (static) and
        // drawn again on top every frame with a flickering alpha so the keys look firelit.
        void DrawGlow(Graphics g, int maxAlpha)
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            Rectangle gr = new Rectangle(0, (int)(h * 0.45f), w, h - (int)(h * 0.45f));
            using (LinearGradientBrush b = new LinearGradientBrush(gr,
                Color.FromArgb(0, 255, 90, 20), Color.FromArgb(maxAlpha, 255, 84, 18), 90f))
                g.FillRectangle(b, gr);
        }

        void DrawFire(Graphics g, float t)
        {
            float flick = 0.6f + 0.4f * (float)(Math.Sin(t * 2.3) * (0.6 + 0.4 * Math.Sin(t * 6.1)));
            DrawGlow(g, (int)(46 * Clamp01(flick)));
        }

        void DrawEmbers(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                foreach (Ember e in embers)
                {
                    float age = 1f - e.Life / e.Max;                       // 0 -> 1
                    float a = (float)Math.Pow(Math.Sin(Math.PI * Clamp01(age)), 0.8);
                    if (a <= 0.02f) continue;
                    Color hot = age < 0.5f
                        ? Lerp(Color.FromArgb(255, 235, 150), Color.FromArgb(255, 110, 24), age * 2f)
                        : Lerp(Color.FromArgb(255, 110, 24), Color.FromArgb(170, 34, 10), (age - 0.5f) * 2f);
                    float flick = 0.8f + 0.2f * (float)Math.Sin(clock.Elapsed.TotalSeconds * 9 + e.Phase * 3);

                    float s = e.Size;
                    b.Color = Color.FromArgb((int)(26 * a * flick), hot);
                    g.FillEllipse(b, e.X - s * 3.2f, e.Y - s * 3.2f, s * 6.4f, s * 6.4f);
                    b.Color = Color.FromArgb((int)(70 * a * flick), hot);
                    g.FillEllipse(b, e.X - s * 1.8f, e.Y - s * 1.8f, s * 3.6f, s * 3.6f);
                    b.Color = Color.FromArgb((int)(215 * a * flick), hot);
                    g.FillEllipse(b, e.X - s * 0.6f, e.Y - s * 0.6f, s * 1.2f, s * 1.2f);
                }
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        // The keys, labels and title are drawn into a cached bitmap that is only refreshed when
        // something changes (hover, press, page animation, resize). Each frame then only blits it
        // and draws the flickering glow and the embers on top, which keeps idle CPU use low.
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (cache == null || cache.Width != ClientSize.Width || cache.Height != ClientSize.Height)
            {
                if (cache != null) cache.Dispose();
                cache = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
                dirty = true;
            }
            if (dirty || animT < AnimDur)
            {
                using (Graphics cg = Graphics.FromImage(cache)) RenderStatic(cg);
                dirty = false;
            }
            g.DrawImageUnscaled(cache, 0, 0);
            DrawFire(g, (float)clock.Elapsed.TotalSeconds);
            DrawEmbers(g);
        }

        void RenderStatic(Graphics g)
        {
            g.Clear(BgColor);

            // title bar
            int ih = (int)(20 * scale);
            Rectangle title = new Rectangle(0, 0, ClientSize.Width, TitleH);
            using (Brush b = new SolidBrush(TitleColor)) g.FillRectangle(b, title);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(titleLogo, new Rectangle(Pad, (TitleH - ih) / 2, ih, ih));
            TextRenderer.DrawText(g, "Mathematical Keyboard", GetFont(UiFont, (int)(13 * scale)),
                new Rectangle(Pad + ih + 6, 0, ClientSize.Width / 2, TitleH), Color.Silver,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (hoverTitle == 1) using (Brush b = new SolidBrush(HoverColor)) g.FillRectangle(b, MinRect);
            if (hoverTitle == 2) using (Brush b = new SolidBrush(Color.FromArgb(0xC4, 0x2B, 0x1C))) g.FillRectangle(b, CloseRect);
            TextRenderer.DrawText(g, "—", GetFont(UiFont, (int)(13 * scale)), MinRect, Color.Silver, Flags);
            TextRenderer.DrawText(g, "✕", GetFont("Segoe UI Symbol", (int)(12 * scale)), CloseRect, Color.Silver, Flags);

            DrawGlow(g, 64);

            float ux = UnitX, uy = UnitY;

            // pass 1: key backgrounds
            foreach (Key k in keys)
            {
                Rectangle r = KeyRect(k);
                r.Inflate(-Gap, -Gap);
                float anim = 1f;
                r.X += (int)AnimOffset(k, ux, out anim);

                Color fill = KeyColor;
                if (k.Act == Act.Shift && shift) fill = AccentA;
                else if (k == down) fill = PressColor;
                else if (k == hover) fill = HoverColor;

                using (Brush b = new SolidBrush(Color.FromArgb((int)(236 * anim), fill)))
                    g.FillRectangle(b, r);
            }

            // animated tab highlight
            float tw = (float)Cols / tabNames.Length;
            Rectangle hr = new Rectangle(
                (int)Math.Round(Pad + hlX * ux) + Gap, TitleH + Pad + Gap,
                (int)Math.Round(tw * ux) - 2 * Gap, (int)Math.Round(uy) - 2 * Gap);
            using (LinearGradientBrush b = new LinearGradientBrush(hr, AccentA, AccentB, 0f))
                g.FillRectangle(b, hr);

            // pass 2: labels
            foreach (Key k in keys)
            {
                Rectangle r = KeyRect(k);
                r.Inflate(-Gap, -Gap);
                float anim = 1f;
                r.X += (int)AnimOffset(k, ux, out anim);

                string text = k.Label;
                if (k.Greek && shift) text = text.ToUpperInvariant();

                bool isText = k.Act != Act.Text || text.Length > 1;
                bool big = k.Act == Act.Back || k.Act == Act.Enter || k.Act == Act.Left || k.Act == Act.Right;
                int px = (int)(uy * (isText && !big ? 0.34f : 0.46f));
                if (page == 5 && k.Act == Act.Text) px = (int)(px * 1.6f);   // superscript glyphs are tiny

                string family = IsAscii(text) ? UiFont : MathFont;
                if (text == "⌫") family = "Segoe UI Symbol";

                Color under = (k.Act == Act.Tab && k.Page == page) ? AccentA : KeyColor;
                Color col = Lerp(under, TextColor, anim);
                TextRenderer.DrawText(g, text, FitFont(g, text, family, px, r.Width), r, col, Flags);
            }

            using (Pen p = new Pen(Color.FromArgb(0x6A, 0x3A, 0x22)))
                g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            // resize grip
            using (Brush b = new SolidBrush(Color.FromArgb(0x88, 0x66, 0x55)))
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j <= i; j++)
                        g.FillRectangle(b, ClientSize.Width - 4 - 4 * j, ClientSize.Height - 4 - 4 * (2 - i), 2, 2);
        }

        // Page change: symbol keys slide in from the travel direction with a staggered fade.
        float AnimOffset(Key k, float ux, out float alpha)
        {
            alpha = 1f;
            if (animT >= AnimDur || k.Act != Act.Text || k.X >= NumpadX) return 0f;
            int col = (int)k.X;
            int row = (int)k.Y - 1;
            float idx = animDir > 0 ? col : Cols - 1 - col;
            float delay = idx * 0.022f + row * 0.02f;
            float p = Clamp01((animT - delay) / 0.22f);
            float e = 1f - (float)Math.Pow(1f - p, 3);
            alpha = e;
            return (1f - e) * animDir * ux * 0.35f;
        }

        // ---------- test helpers ----------

        public void Snapshot(string path)
        {
            IntPtr h = Handle;
            using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }

        public void SetState(int pageIndex, bool shiftOn, float animTime)
        {
            frameTimer.Stop();
            page = pageIndex;
            shift = shiftOn;
            Build();
            hlX = page * ((float)Cols / tabNames.Length);
            animT = animTime;
            animDir = 1;
            dirty = true;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // --icon <ico> [png]  : write the app icon (and a large PNG preview) and exit
            if (args.Length >= 2 && args[0] == "--icon")
            {
                Logo.WriteIco(args[1]);
                if (args.Length > 2)
                    using (Bitmap b = Logo.Render(512)) b.Save(args[2], ImageFormat.Png);
                return;
            }

            // --snap <png> [page] [shift|-] [animSeconds]  : render to an image and exit
            if (args.Length >= 2 && args[0] == "--snap")
            {
                KeyboardForm f = new KeyboardForm();
                f.SetState(args.Length > 2 ? int.Parse(args[2]) : 0, args.Length > 3 && args[3] == "shift",
                    args.Length > 4 ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 9f);
                f.Snapshot(args[1]);
                return;
            }

            // --send <text> <delayMs>  : type text into the focused app after a delay (SendInput self-test)
            if (args.Length >= 2 && args[0] == "--send")
            {
                System.Threading.Thread.Sleep(args.Length > 2 ? int.Parse(args[2]) : 2000);
                Native.SendText(args[1]);
                return;
            }

            Application.Run(new KeyboardForm());
        }
    }
}
