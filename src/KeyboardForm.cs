using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MathKeyboard
{
    class KeyboardForm : Form
    {
        const int WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008;

        static readonly Color AccentA = Color.FromArgb(0xF0, 0x70, 0x1A);
        static readonly Color AccentB = Color.FromArgb(0xC0, 0x2C, 0x10);
        static readonly Color TextColor = Color.FromArgb(0xFA, 0xF4, 0xEF);

        const string MathFont = "Cambria Math";
        const string UiFont = "Segoe UI";
        const string SymbolFont = "Segoe UI Symbol";
        const string SymbolChars = "⌫↵⇧✕⇌⇄⇀↑↓";
        const float NumpadX = 10.4f;
        const float TotalW = 14.4f;
        const int Rows = 6;
        const int Cols = 10;
        const int PerPage = 40;
        const int GreekPage = 2;
        const float AnimDur = 0.55f;

        readonly string[] tabNames =
        {
            "∫ Calculus", "∑ Algebra", "π Greek", "sin Trig",
            "≤ Logic", "x² Powers", "ℏ Physics", "H₂O Chem"
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
        readonly StringFormat sfCenter, sfLeft;

        Native.Surface surface;
        Bitmap cache;
        bool dirty = true;

        int page;
        bool shift;
        bool italicOn = true;
        bool eqMode;
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
            ClientSize = new Size((int)(860 * scale), (int)(290 * scale));
            MinimumSize = new Size((int)(600 * scale), (int)(220 * scale));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(16 * scale));

            sfCenter = new StringFormat(StringFormat.GenericTypographic);
            sfCenter.Alignment = StringAlignment.Center;
            sfCenter.LineAlignment = StringAlignment.Center;
            sfCenter.FormatFlags |= StringFormatFlags.NoWrap;
            sfLeft = new StringFormat(sfCenter);
            sfLeft.Alignment = StringAlignment.Near;

            using (Bitmap small = Logo.Render(32))
                Icon = Icon.FromHandle(small.GetHicon());
            titleLogo = Logo.Render((int)(40 * scale));

            BuildPages();
            Build();
            SpawnInitialEmbers();

            repeatTimer.Tick += delegate
            {
                repeatTimer.Interval = 45;
                if (down != null)
                {
                    repeating = true;
                    Press(down);
                    repeating = false;
                }
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
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableBlur(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyRegion();
            dirty = true;
            Present();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (titleLogo == null || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
            ApplyRegion();
            dirty = true;
            Present();
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        void ApplyRegion()
        {
            using (GraphicsPath p = RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), Radius))
                Region = new Region(p);
        }

        float Radius { get { return 18 * scale; } }

        // ---------- symbol pages ----------

        static void S(List<Key> l, string chars, bool math)
        {
            foreach (char c in chars)
                if (c != ' ') l.Add(new Key { Label = c.ToString(), Out = c.ToString(), Math = math });
        }

        static void S(List<Key> l, string chars) { S(l, chars, false); }

        // Structured key: plain Unicode text normally, a linear-format template in Equation mode.
        static void TE(List<Key> l, string label, string plain, string eq)
        {
            l.Add(new Key { Label = label, Out = plain, Eq = eq });
        }

        static void T(List<Key> l, string label, string output) { T(l, label, output, false); }

        static void T(List<Key> l, string label, string output, bool math)
        {
            l.Add(new Key { Label = label, Out = output, Math = math });
        }

        void BuildPages()
        {
            List<Key> p = new List<Key>();

            // 0: calculus
            // Keys marked TE(label, plain text, equation template): in Equation mode the template is
            // typed so Word/PowerPoint/OneNote build a real limit / integral / sum / fraction.
            S(p, "∫ ∬ ∭ ∮ ∯ ∂ ∇ ∞ ′ ″");
            TE(p, "lim", "lim", "lim ");
            TE(p, "lim x→0", "limₓ→₀", "lim_(x→0) ");
            TE(p, "lim x→∞", "limₓ→∞", "lim_(x→∞) ");
            TE(p, "lim x→a", "limₓ→ₐ", "lim_(x→a) ");
            TE(p, "∫ₐᵇ", "∫ₐᵇ", "∫_a^b ");
            TE(p, "∑ᵢ₌₁ⁿ", "∑ᵢ₌₁ⁿ", "∑_(i=1)^n ");
            TE(p, "∏ᵢ₌₁ⁿ", "∏ᵢ₌₁ⁿ", "∏_(i=1)^n ");
            T(p, "dx", "dx"); T(p, "dy", "dy"); T(p, "dt", "dt");
            TE(p, "d/dx", "d/dx", "d/dx ");
            TE(p, "dy/dx", "dy/dx", "dy/dx ");
            TE(p, "∂/∂x", "∂/∂x", "∂/∂x ");
            TE(p, "∂²/∂x²", "∂²/∂x²", "∂^2/∂x^2 ");
            T(p, "f(x)", "f(x)"); T(p, "f′(x)", "f′(x)");
            S(p, "Δ");
            S(p, "δ ε", true);
            S(p, "∑ √ ∝ ±");
            T(p, "dz", "dz"); T(p, "dθ", "dθ"); T(p, "−∞", "−∞");
            T(p, "x→∞", "x→∞"); T(p, "x→0", "x→0");
            S(p, "≈");
            pages.Add(p.ToArray());

            // 1: algebra
            p = new List<Key>();
            S(p, "∑ ∏ √ ∛ ! ^ _ / [ ]");
            TE(p, "√x", "√x", "√(x) ");
            TE(p, "ⁿ√x", "ⁿ√x", "√(n&x) ");
            TE(p, "a/b", "a/b", "a/b ");
            TE(p, "xⁿ", "xⁿ", "x^n ");
            TE(p, "xₙ", "xₙ", "x_n ");
            TE(p, "|x|", "|x|", "|x|");
            S(p, "± ∓ × ÷");
            S(p, "· ∘ % ° ∠ ⟂");
            S(p, "x y z n", true);
            S(p, "a b", true);
            S(p, "⌊ ⌋ ⌈ ⌉ { } < >");
            pages.Add(p.ToArray());

            // 2: greek
            p = new List<Key>();
            S(p, "α β γ δ ε ζ η θ ι κ");
            S(p, "λ μ ν ξ ο π ρ σ τ υ");
            S(p, "φ χ ψ ω ϕ ϑ ϱ ϖ ς ϵ");
            foreach (Key k in p) { k.Greek = true; k.Math = true; }
            pages.Add(p.ToArray());

            // 3: trig
            p = new List<Key>();
            foreach (string f in new[] { "sin", "cos", "tan", "cot", "sec", "csc" }) T(p, f, f + "(");
            foreach (string f in new[] { "sin", "cos", "tan" }) T(p, f + "⁻¹", f + "⁻¹(");
            T(p, "π", "π", true);
            foreach (string f in new[] { "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh" }) T(p, f, f + "(");
            T(p, "θ", "θ", true); T(p, "°", "°"); T(p, "rad", "rad"); T(p, "e", "e");
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

            // 6: physics
            p = new List<Key>();
            S(p, "ℏ ħ ∇ ∂ ∮ ∝ ≈ ∞ ± ∓");
            S(p, "Δ Ω");
            S(p, "ω λ μ ν ρ σ τ ε", true);
            S(p, "° Å ℃ ‰ · ⟨ ⟩ ⊥ ∥");
            T(p, "a⃗", "⃗");
            foreach (string u in new[] { "kg", "m/s", "m/s²", "N", "J", "W", "Hz", "Pa", "eV", "mol" }) T(p, u, u);
            pages.Add(p.ToArray());

            // 7: chemistry
            p = new List<Key>();
            S(p, "→ ⇌ ⇄ ↑ ↓ Δ");
            foreach (string s in new[] { "(s)", "(l)", "(g)", "(aq)" }) T(p, s, s);
            S(p, "₀ ₁ ₂ ₃ ₄ ₅ ₆ ₇ ₈ ₉");
            S(p, "⁺ ⁻ ² ³ ⁴ ⁵ ⁶ ⁷ ⁸ ⁹");
            foreach (string f in new[] { "H₂O", "CO₂", "O₂", "H₂", "N₂", "NH₃", "CH₄", "NaCl", "HCl", "H₂SO₄" })
                T(p, f, f);
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

            keys.Add(new Key { Label = char.ConvertFromUtf32(0x1D465) + " Italic", Act = Act.Italic, X = 0, Y = 5, W = 1.7f });
            keys.Add(new Key { Label = "∑ Equation", Act = Act.Eq, X = 1.7f, Y = 5, W = 1.7f });
            if (page == GreekPage)
            {
                keys.Add(new Key { Label = "⇧ Shift", Act = Act.Shift, X = 3.4f, Y = 5, W = 1.7f });
                keys.Add(new Key { Label = "Space", Act = Act.Space, X = 5.1f, Y = 5, W = 1.9f, Repeat = true });
            }
            else
            {
                keys.Add(new Key { Label = "Space", Act = Act.Space, X = 3.4f, Y = 5, W = 3.6f, Repeat = true });
            }
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
        Rectangle SndRect { get { return new Rectangle(MinRect.Left - TitleH * 3 / 2, 0, TitleH * 3 / 2, TitleH); } }

        static string SettingsPath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MathKeyboard", "settings.txt");
            }
        }

        public static void LoadSettings()
        {
            try { Audio.Muted = System.IO.File.Exists(SettingsPath) && System.IO.File.ReadAllText(SettingsPath).Contains("muted=1"); }
            catch { }
        }

        static void SaveSettings()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
                System.IO.File.WriteAllText(SettingsPath, Audio.Muted ? "muted=1" : "muted=0");
            }
            catch { }
        }

        Rectangle CloseRect { get { return new Rectangle(ClientSize.Width - TitleH * 3 / 2, 0, TitleH * 3 / 2, TitleH); } }

        Key HitTest(Point p)
        {
            foreach (Key k in keys)
                if (KeyRect(k).Contains(p)) return k;
            return null;
        }

        // ---------- behaviour ----------

        string ShownText(Key k)
        {
            if (k.Greek && shift) return k.Label.ToUpperInvariant();
            if (italicOn && k.Math) return MathText.Italic(k.Label);
            return k.Label;
        }

        // In Equation mode the structured keys type the Word/PowerPoint/OneNote linear format
        // (e.g. "lim_(x→0) "), which those apps turn into a real typeset equation. Plain letters are
        // typed as plain letters there because the equation editor italicises them itself.
        string Typed(Key k)
        {
            if (eqMode && k.Eq != null) return k.Eq;
            if (k.Greek && shift) return k.Out.ToUpperInvariant();
            if (italicOn && k.Math && !eqMode) return MathText.Italic(k.Out);
            return k.Out;
        }

        void SetPage(int newPage)
        {
            if (newPage == page) return;
            animDir = newPage > page ? 1 : -1;
            page = newPage;
            if (page != GreekPage) shift = false;   // Shift only exists on the Greek page
            animT = 0;
            Build();
        }

        int lastVariant;
        bool repeating;
        int upKind;   // 0 none, 1 normal key, 2 big key: which release sound to play on mouse up

        // Picks one of the pitch variants, never the same twice in a row.
        int Variant()
        {
            int i;
            do { i = rng.Next(Sfx.Down.Length); } while (i == lastVariant);
            lastVariant = i;
            return i;
        }

        void PlayDown(bool big)
        {
            Audio.Play((big ? Sfx.BigDown : Sfx.Down)[Variant()], repeating ? 0.5f : 1f);
            if (!repeating) upKind = big ? 2 : 1;
        }

        void Press(Key k)
        {
            switch (k.Act)
            {
                case Act.Text: Native.SendText(Typed(k)); PlayDown(false); break;
                case Act.Back: Native.SendVk(0x08, false); PlayDown(true); break;
                case Act.Enter: Native.SendVk(0x0D, false); PlayDown(true); break;
                case Act.Space: Native.SendVk(0x20, false); PlayDown(true); break;
                case Act.Left: Native.SendVk(0x25, true); PlayDown(false); break;
                case Act.Right: Native.SendVk(0x27, true); PlayDown(false); break;
                case Act.Shift: shift = !shift; PlayDown(false); break;
                case Act.Italic: italicOn = !italicOn; PlayDown(false); break;
                case Act.Eq:
                    eqMode = !eqMode;
                    // inside Word / PowerPoint / OneNote, turning Equation mode on also starts an equation (Alt+=)
                    if (eqMode && Native.ForegroundIsOffice()) Native.SendCombo(0x12, 0xBB);
                    PlayDown(false);
                    break;
                case Act.Tab:
                    down = null;
                    PlayDown(false);
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
            if (SndRect.Contains(e.Location))
            {
                Audio.Muted = !Audio.Muted;
                SaveSettings();
                if (!Audio.Muted) Audio.Play(Sfx.Down[2], 0.8f);
                dirty = true;
                return;
            }

            Key k = HitTest(e.Location);
            if (k == null) return;
            down = k;
            dirty = true;
            upKind = 0;
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
            if (upKind != 0)
            {
                Audio.Play((upKind == 2 ? Sfx.BigUp : Sfx.Up)[Variant()], 0.8f);
                upKind = 0;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Key h = HitTest(e.Location);
            int t = CloseRect.Contains(e.Location) ? 2 : MinRect.Contains(e.Location) ? 1 : SndRect.Contains(e.Location) ? 3 : 0;
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
                if (p.Y >= 0 && p.Y < TitleH && p.X < SndRect.Left) { m.Result = (IntPtr)2; return; }
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
            if (WindowState == FormWindowState.Minimized || !IsHandleCreated) return;
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, now - lastTime);
            lastTime = now;
            Advance(dt);
            Present();
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

        static bool IsAscii(string s)
        {
            foreach (char c in s) if (c > 127) return false;
            return true;
        }

        static bool IsScript(char c)
        {
            return c == '²' || c == '³' || c == '¹' || (c >= '⁰' && c <= '₟') ||
                c == 'ˣ' || c == 'ʸ' || c == 'ᶻ' || c == 'ᵢ' || c == 'ⱼ';
        }

        static string FamilyFor(string text)
        {
            foreach (char c in text)
                if (SymbolChars.IndexOf(c) >= 0) return SymbolFont;
            return IsAscii(text) ? UiFont : MathFont;
        }

        Font FitFont(Graphics g, string text, string family, int maxPx, int width)
        {
            string id = family + "|" + maxPx + "|" + width + "|" + text;
            Font f;
            if (fitCache.TryGetValue(id, out f)) return f;
            int px = maxPx;
            f = GetFont(family, px);
            while (px > 9 && g.MeasureString(text, f, 10000, sfCenter).Width > width - 8)
                f = GetFont(family, --px);
            fitCache[id] = f;
            return f;
        }

        void DrawText(Graphics g, string text, string family, int px, Rectangle r, Color c, StringFormat sf)
        {
            Font f = FitFont(g, text, family, px, r.Width);
            using (Brush b = new SolidBrush(c)) g.DrawString(text, f, b, r, sf);
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        static Color A(float alpha, int r, int g, int b)
        {
            return Color.FromArgb((int)Math.Max(0, Math.Min(255, alpha)), r, g, b);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(0.1f, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Warm glow rising from the bottom edge: baked into the cached layer, and drawn again
        // on top every frame with a flickering alpha so the glass looks firelit.
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
            Region old = g.Clip;
            using (GraphicsPath clip = RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), Radius))
                g.SetClip(clip);
            DrawGlow(g, (int)(46 * Clamp01(flick)));
            g.Clip = old;
        }

        void DrawEmbers(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double now = clock.Elapsed.TotalSeconds;
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                foreach (Ember e in embers)
                {
                    float age = 1f - e.Life / e.Max;                       // 0 -> 1
                    float a = (float)Math.Pow(Math.Sin(Math.PI * Clamp01(age)), 0.8);
                    if (a <= 0.02f) continue;
                    Color hot = age < 0.5f
                        ? Mix(Color.FromArgb(255, 235, 150), Color.FromArgb(255, 110, 24), age * 2f)
                        : Mix(Color.FromArgb(255, 110, 24), Color.FromArgb(170, 34, 10), (age - 0.5f) * 2f);
                    float flick = 0.8f + 0.2f * (float)Math.Sin(now * 9 + e.Phase * 3);

                    float s = e.Size;
                    b.Color = Color.FromArgb((int)(26 * a * flick), hot);
                    g.FillEllipse(b, e.X - s * 3.2f, e.Y - s * 3.2f, s * 6.4f, s * 6.4f);
                    b.Color = Color.FromArgb((int)(70 * a * flick), hot);
                    g.FillEllipse(b, e.X - s * 1.8f, e.Y - s * 1.8f, s * 3.6f, s * 3.6f);
                    b.Color = Color.FromArgb((int)(215 * a * flick), hot);
                    g.FillEllipse(b, e.X - s * 0.6f, e.Y - s * 0.6f, s * 1.2f, s * 1.2f);
                }
            }
        }

        static Color Mix(Color a, Color b, float t)
        {
            t = Clamp01(t);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void EnsureSurface()
        {
            if (surface != null && surface.Width == ClientSize.Width && surface.Height == ClientSize.Height) return;
            if (surface != null) surface.Dispose();
            surface = new Native.Surface(ClientSize.Width, ClientSize.Height);
            surface.G.SmoothingMode = SmoothingMode.AntiAlias;
            surface.G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            surface.G.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        // Cached layer (glass panel, keys, labels) is only rebuilt when something changes; every frame
        // it is blitted into the window surface and the flickering glow and embers go on top.
        void RenderFrame()
        {
            EnsureSurface();
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

            Graphics g = surface.G;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(cache, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            DrawFire(g, (float)clock.Elapsed.TotalSeconds);
            DrawEmbers(g);
        }

        void Present()
        {
            if (!IsHandleCreated) return;
            RenderFrame();
            surface.Present(Handle);
        }

        // ---- liquid glass primitives ----

        // state: 0 normal, 1 hover, 2 pressed, 3 active (ember)
        void GlassKey(Graphics g, Rectangle r, float e, int state)
        {
            if (r.Width < 2 || r.Height < 2) return;
            float rad = Math.Min(9 * scale, r.Height / 3f);
            using (GraphicsPath p = RoundRect(r, rad))
            {
                using (Brush b = new SolidBrush(A(48 * e, 0, 0, 0))) g.FillPath(b, p);

                if (state >= 2)
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(r,
                        A((state == 2 ? 225 : 195) * e, AccentA.R, AccentA.G, AccentA.B),
                        A((state == 2 ? 225 : 195) * e, AccentB.R, AccentB.G, AccentB.B), 90f))
                        g.FillPath(b, p);
                }
                else
                {
                    float a1 = state == 1 ? 70 : 44, a2 = state == 1 ? 36 : 14;
                    using (LinearGradientBrush b = new LinearGradientBrush(r, A(a1 * e, 255, 255, 255), A(a2 * e, 255, 255, 255), 90f))
                        g.FillPath(b, p);
                    if (state == 1)
                        using (Brush b = new SolidBrush(A(34 * e, 255, 140, 60))) g.FillPath(b, p);
                }

                using (LinearGradientBrush pb = new LinearGradientBrush(r, A(135 * e, 255, 255, 255), A(26 * e, 255, 255, 255), 90f))
                using (Pen pen = new Pen(pb, 1f))
                    g.DrawPath(pen, p);

                // specular sheen along the top edge
                float y = r.Y + 1.6f;
                using (LinearGradientBrush sb = new LinearGradientBrush(
                    new RectangleF(r.X + rad * 0.6f, y - 1, Math.Max(2, r.Width - rad * 1.2f), 2),
                    A(0, 255, 255, 255), A(0, 255, 255, 255), 0f))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new[] { A(0, 255, 255, 255), A(120 * e, 255, 255, 255), A(0, 255, 255, 255) };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    sb.InterpolationColors = cb;
                    using (Pen pen = new Pen(sb, 1f))
                        g.DrawLine(pen, r.X + rad * 0.6f, y, r.Right - rad * 0.6f, y);
                }
            }
        }

        void RenderStatic(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int W = ClientSize.Width, H = ClientSize.Height;
            RectangleF panel = new RectangleF(0.5f, 0.5f, W - 1, H - 1);

            using (GraphicsPath pp = RoundRect(panel, Radius))
            {
                // frosted tint over the blurred backdrop
                using (LinearGradientBrush b = new LinearGradientBrush(panel, A(150, 34, 26, 24), A(122, 14, 10, 9), 90f))
                    g.FillPath(b, pp);

                g.SetClip(pp);
                RectangleF top = new RectangleF(0, 0, W, H * 0.5f);
                using (LinearGradientBrush b = new LinearGradientBrush(top, A(46, 255, 255, 255), A(0, 255, 255, 255), 90f))
                    g.FillRectangle(b, top);
                DrawGlow(g, 70);
                g.ResetClip();

                // glass rim: bright top-left, warm bottom-right
                using (LinearGradientBrush rb = new LinearGradientBrush(panel, Color.White, Color.White, 55f))
                {
                    ColorBlend cb = new ColorBlend(4);
                    cb.Colors = new[] { A(210, 255, 255, 255), A(40, 255, 255, 255), A(36, 255, 210, 170), A(170, 255, 130, 50) };
                    cb.Positions = new[] { 0f, 0.4f, 0.65f, 1f };
                    rb.InterpolationColors = cb;
                    using (Pen pen = new Pen(rb, Math.Max(1.2f, 1.5f * scale)))
                        g.DrawPath(pen, pp);
                }
            }

            // title bar
            int ih = (int)(20 * scale);
            g.DrawImage(titleLogo, new Rectangle(Pad + 2, (TitleH - ih) / 2, ih, ih));
            DrawText(g, "Mathematical Keyboard", UiFont, (int)(13 * scale),
                new Rectangle(Pad + ih + 8, 0, ClientSize.Width / 2, TitleH), A(215, 238, 232, 226), sfLeft);
            if (hoverTitle == 1) using (GraphicsPath p = RoundRect(Rectangle.Inflate(MinRect, -3, -4), 7 * scale))
                using (Brush b = new SolidBrush(A(70, 255, 255, 255))) g.FillPath(b, p);
            if (hoverTitle == 2) using (GraphicsPath p = RoundRect(Rectangle.Inflate(CloseRect, -3, -4), 7 * scale))
                using (Brush b = new SolidBrush(A(215, 215, 50, 30))) g.FillPath(b, p);
            if (hoverTitle == 3) using (GraphicsPath p = RoundRect(Rectangle.Inflate(SndRect, -3, -4), 7 * scale))
                using (Brush b = new SolidBrush(A(70, 255, 255, 255))) g.FillPath(b, p);
            DrawText(g, "♪", SymbolFont, (int)(14 * scale), SndRect, A(Audio.Muted ? 90 : 225, 235, 230, 225), sfCenter);
            if (Audio.Muted)
                using (Pen pen = new Pen(A(210, 235, 120, 90), Math.Max(1.5f, 1.6f * scale)))
                    g.DrawLine(pen, SndRect.Left + SndRect.Width * 0.3f, TitleH * 0.72f, SndRect.Right - SndRect.Width * 0.3f, TitleH * 0.28f);
            DrawText(g, "—", UiFont, (int)(13 * scale), MinRect, A(225, 235, 230, 225), sfCenter);
            DrawText(g, "✕", SymbolFont, (int)(12 * scale), CloseRect, A(225, 235, 230, 225), sfCenter);
            using (Pen pen = new Pen(A(26, 255, 255, 255)))
                g.DrawLine(pen, Pad, TitleH, W - Pad, TitleH);

            float ux = UnitX, uy = UnitY;

            // pass 1: key glass
            foreach (Key k in keys)
            {
                Rectangle r = KeyRect(k);
                r.Inflate(-Gap, -Gap);
                float e;
                r.X += (int)AnimOffset(k, ux, out e);

                int state = 0;
                if ((k.Act == Act.Shift && shift) || (k.Act == Act.Italic && italicOn) || (k.Act == Act.Eq && eqMode)) state = 3;
                else if (k == down) state = 2;
                else if (k == hover) state = 1;
                GlassKey(g, r, e, state);
            }

            // animated tab highlight
            float tw = (float)Cols / tabNames.Length;
            Rectangle hr = new Rectangle(
                (int)Math.Round(Pad + hlX * ux) + Gap, TitleH + Pad + Gap,
                (int)Math.Round(tw * ux) - 2 * Gap, (int)Math.Round(uy) - 2 * Gap);
            GlassKey(g, hr, 1f, 3);

            // pass 2: labels
            foreach (Key k in keys)
            {
                Rectangle r = KeyRect(k);
                r.Inflate(-Gap, -Gap);
                float e;
                r.X += (int)AnimOffset(k, ux, out e);

                string text = ShownText(k);
                bool isText = k.Act != Act.Text || (text.Length > 1 && !char.IsHighSurrogate(text[0]));
                bool big = k.Act == Act.Back || k.Act == Act.Enter || k.Act == Act.Left || k.Act == Act.Right;
                int px = (int)(uy * (isText && !big ? 0.34f : 0.46f));
                if (k.Act == Act.Text && text.Length == 1 && IsScript(text[0])) px = (int)(px * 1.55f);   // tiny script glyphs
                else if (k.Act == Act.Text && text.Length == 2 && char.IsHighSurrogate(text[0])) px = (int)(px * 1.2f);   // math italic

                DrawText(g, text, FamilyFor(text), px, r, A(255 * e, TextColor.R, TextColor.G, TextColor.B), sfCenter);
            }
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

        // Renders one frame over a test backdrop (the real window blurs whatever is behind it).
        public void Snapshot(string path, string backdrop)
        {
            RenderFrame();
            int w = surface.Width, h = surface.Height;
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                if (backdrop == "light") g.Clear(Color.FromArgb(235, 238, 242));
                else if (backdrop == "dark") g.Clear(Color.FromArgb(20, 22, 30));
                else
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                        Color.FromArgb(40, 120, 220), Color.FromArgb(230, 70, 140), 20f))
                        g.FillRectangle(b, 0, 0, w, h);
                    using (Brush b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                        for (int x = 0; x < w; x += 90) g.FillRectangle(b, x, 0, 28, h);
                }
                g.DrawImage(surface.Bmp, 0, 0);
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
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ev)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mathkeyboard-error.txt"),
                    ev.ExceptionObject.ToString());
                Environment.Exit(1);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
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

            // --snap <png> [page] [shift|-] [animSeconds] [color|light|dark]  : render to an image and exit
            if (args.Length >= 2 && args[0] == "--snap")
            {
                try
                {
                    KeyboardForm f = new KeyboardForm();
                    f.SetState(args.Length > 2 ? int.Parse(args[2]) : 0, args.Length > 3 && args[3] == "shift",
                        args.Length > 4 ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 9f);
                    f.Snapshot(args[1], args.Length > 5 ? args[5] : "color");
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(args[1] + ".err", ex.ToString());
                }
                return;
            }

            // --sounds <wav>  : write a preview of all sound effects, play them once, log mixer health
            if (args.Length >= 2 && args[0] == "--sounds")
            {
                Audio.WritePreview(args[1]);
                Audio.Init();
                for (int i = 0; i < 8; i++) { Audio.Play(Sfx.Down[i % 6], 1f); System.Threading.Thread.Sleep(70); Audio.Play(Sfx.Up[i % 6], 0.8f); System.Threading.Thread.Sleep(90); }
                Audio.Play(Sfx.BigDown[2], 1f); System.Threading.Thread.Sleep(900);
                System.IO.File.WriteAllText(args[1] + ".log",
                    "ready=" + Audio.Ready + " chunks=" + Audio.Chunks + " writeErrors=" + Audio.WriteErrors);
                return;
            }

            // --send <text> <delayMs>  : type text into the focused app after a delay (SendInput self-test)
            if (args.Length >= 2 && args[0] == "--send")
            {
                System.Threading.Thread.Sleep(args.Length > 2 ? int.Parse(args[2]) : 2000);
                Native.SendText(args[1]);
                return;
            }

            KeyboardForm.LoadSettings();
            Audio.Init();
            Application.Run(new KeyboardForm());
        }
    }
}

