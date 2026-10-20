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
    static class Logo
    {
        static GraphicsPath Flame(float x, float y, float w, float h)
        {
            Func<float, float, PointF> P = (px, py) => new PointF(x + px * w, y + py * h);
            GraphicsPath path = new GraphicsPath();
            path.AddBezier(P(0.50f, 0.98f), P(0.08f, 0.90f), P(0.06f, 0.52f), P(0.36f, 0.32f));
            path.AddBezier(P(0.36f, 0.32f), P(0.50f, 0.22f), P(0.54f, 0.10f), P(0.46f, 0.00f));
            path.AddBezier(P(0.46f, 0.00f), P(0.80f, 0.16f), P(0.96f, 0.46f), P(0.92f, 0.68f));
            path.AddBezier(P(0.92f, 0.68f), P(0.88f, 0.90f), P(0.70f, 0.98f), P(0.50f, 0.98f));
            path.CloseFigure();
            return path;
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Bitmap Render(int size)
        {
            const int S = 512;
            using (Bitmap big = new Bitmap(S, S, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    RectangleF tile = new RectangleF(8, 8, S - 16, S - 16);
                    using (GraphicsPath tp = RoundRect(tile, 108))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(tile,
                            Color.FromArgb(0x2E, 0x1B, 0x14), Color.FromArgb(0x10, 0x0A, 0x08), 90f))
                            g.FillPath(b, tp);

                        // warm glow rising from the bottom
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            gp.AddEllipse(-S * 0.1f, S * 0.55f, S * 1.2f, S * 0.8f);
                            using (PathGradientBrush pg = new PathGradientBrush(gp))
                            {
                                pg.CenterColor = Color.FromArgb(150, 255, 90, 20);
                                pg.SurroundColors = new[] { Color.FromArgb(0, 255, 90, 20) };
                                GraphicsState st = g.Save();
                                g.SetClip(tp);
                                g.FillPath(pg, gp);
                                g.Restore(st);
                            }
                        }

                        using (Pen pen = new Pen(Color.FromArgb(200, 255, 120, 30), 10))
                            g.DrawPath(pen, tp);
                    }

                    // flame
                    RectangleF fr = new RectangleF(S * 0.17f, S * 0.10f, S * 0.66f, S * 0.80f);
                    using (GraphicsPath fp = Flame(fr.X, fr.Y, fr.Width, fr.Height))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(fr,
                            Color.FromArgb(0xFF, 0xC8, 0x4A), Color.FromArgb(0xD8, 0x2E, 0x0C), 90f))
                        {
                            ColorBlend cb = new ColorBlend(3);
                            cb.Colors = new[] { Color.FromArgb(0xFF, 0xD2, 0x55), Color.FromArgb(0xFF, 0x7E, 0x1F), Color.FromArgb(0xD8, 0x2E, 0x0C) };
                            cb.Positions = new[] { 0f, 0.5f, 1f };
                            b.InterpolationColors = cb;
                            g.FillPath(b, fp);
                        }
                    }

                    // hot core
                    RectangleF cr = new RectangleF(S * 0.30f, S * 0.40f, S * 0.40f, S * 0.48f);
                    using (GraphicsPath cp = Flame(cr.X, cr.Y, cr.Width, cr.Height))
                    using (LinearGradientBrush b = new LinearGradientBrush(cr,
                        Color.FromArgb(235, 0xFF, 0xF1, 0xB0), Color.FromArgb(235, 0xFF, 0xB0, 0x38), 90f))
                        g.FillPath(b, cp);

                    // sigma
                    using (GraphicsPath sp = new GraphicsPath())
                    {
                        sp.AddString("∑", new FontFamily("Cambria Math"), (int)FontStyle.Regular, 300f,
                            new PointF(0, 0), StringFormat.GenericTypographic);
                        RectangleF sb = sp.GetBounds();
                        float target = S * 0.34f;
                        float k = target / sb.Height;
                        using (Matrix m = new Matrix())
                        {
                            m.Translate(S * 0.5f - (sb.X + sb.Width / 2) * k, S * 0.62f - (sb.Y + sb.Height / 2) * k);
                            m.Scale(k, k);
                            // Translate-then-scale (prepend) applies the scale first, then the shift.
                            sp.Transform(m);
                        }
                        using (Brush b = new SolidBrush(Color.FromArgb(0x24, 0x10, 0x0A)))
                            g.FillPath(b, sp);
                    }

                    // a few sparks
                    using (Brush b = new SolidBrush(Color.FromArgb(230, 255, 190, 80)))
                    {
                        g.FillEllipse(b, S * 0.80f, S * 0.18f, 16, 16);
                        g.FillEllipse(b, S * 0.14f, S * 0.30f, 12, 12);
                        g.FillEllipse(b, S * 0.86f, S * 0.40f, 10, 10);
                    }
                }

                Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, size, size));
                }
                return result;
            }
        }

        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap bmp = Render(s))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }

            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length);
                    w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] b in pngs) w.Write(b);
            }
        }
    }
}
