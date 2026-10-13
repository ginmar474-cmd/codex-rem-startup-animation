using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace RemCodexLauncher
{
    public sealed class Settings
    {
        public double Duration = 8.4;
        public int FrameRate = 60;
        public string AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        public string Exe = "";
        public string Arguments = "";
        public string ReferenceImage = "assets/rem-reference.jpg";
        public bool RespectReducedMotion = true;

        public static Settings Load(string root)
        {
            var result = new Settings();
            string file = Path.Combine(root, "launcher.config.xml");
            if (!File.Exists(file)) return result;
            XElement xml = XDocument.Load(file).Root;
            if (xml == null) throw new InvalidDataException("启动配置为空。");
            Func<string, string, string> value = delegate(string name, string fallback)
            {
                XElement element = xml.Element(name);
                return element == null ? fallback : element.Value.Trim();
            };
            result.Duration = double.Parse(value("durationSeconds", "8.4"), CultureInfo.InvariantCulture);
            result.FrameRate = int.Parse(value("frameRate", "60"), CultureInfo.InvariantCulture);
            result.AppId = value("appUserModelId", result.AppId);
            result.Exe = value("executablePath", "");
            result.Arguments = value("executableArguments", "");
            result.ReferenceImage = value("referenceImage", result.ReferenceImage);
            result.RespectReducedMotion = bool.Parse(value("respectReducedMotion", "true"));
            if (result.Duration < 1 || result.Duration > 30) throw new InvalidDataException("durationSeconds 必须为 1 到 30 秒。");
            if (result.FrameRate < 1 || result.FrameRate > 60) throw new InvalidDataException("frameRate 必须为 1 到 60。");
            if (string.IsNullOrWhiteSpace(result.AppId) && string.IsNullOrWhiteSpace(result.Exe))
                throw new InvalidDataException("请配置 appUserModelId 或 executablePath。");
            return result;
        }
    }

    public static class AppActivation
    {
        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ActivationManager { }

        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivationManager
        {
            [PreserveSig]
            int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
                [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint pid);
            [PreserveSig]
            int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string id,
                IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint pid);
            [PreserveSig]
            int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string id,
                IntPtr items, out uint pid);
        }

        public static uint Open(Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Exe))
            {
                string exe = Environment.ExpandEnvironmentVariables(settings.Exe);
                if (!File.Exists(exe)) throw new FileNotFoundException("找不到配置的桌面应用。", exe);
                Process process = Process.Start(new ProcessStartInfo(exe, settings.Arguments) { UseShellExecute = true });
                return process == null ? 0 : (uint)process.Id;
            }
            var activation = (IActivationManager)new ActivationManager();
            try
            {
                uint pid;
                int result = activation.ActivateApplication(settings.AppId, "", 0, out pid);
                Marshal.ThrowExceptionForHR(result);
                return pid;
            }
            finally { Marshal.ReleaseComObject(activation); }
        }
    }

    public sealed class RemScene : FrameworkElement
    {
        public const double DesignWidth = 1280, DesignHeight = 720;
        public const int ExportWidth = 1920, ExportHeight = 1080;
        private readonly Settings settings;
        private readonly bool reducedMotion;
        private readonly BitmapImage reference;
        private readonly Typeface display = new Typeface(new FontFamily("DengXian, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private readonly Typeface normal = new Typeface("Microsoft YaHei UI");
        private readonly Typeface mono = new Typeface("Consolas");
        private readonly Geometry codexGlyph, cnGlyph, enGlyph;
        public double Time;
        public bool Launching;

        public RemScene(string root, Settings config, bool reduced)
        {
            settings = config;
            reducedMotion = reduced;
            Width = DesignWidth;
            Height = DesignHeight;
            reference = LoadImage(root, config.ReferenceImage);
            codexGlyph = TextGeometry("Codex", display, 108, 792, 224, 0.5);
            cnGlyph = TextGeometry("欢迎回来，主人", display, 28, 802, 354, 2.6);
            enGlyph = TextGeometry("REM STARTUP PROTOCOL", display, 13, 804, 399, 1.3);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        private static BitmapImage LoadImage(string root, string relative)
        {
            string file = Path.GetFullPath(Path.Combine(root, relative));
            if (!File.Exists(file)) throw new FileNotFoundException("找不到雷姆参考图素材。", file);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public void SetTime(double seconds)
        {
            double quantized = Math.Floor(Math.Max(0, seconds) * settings.FrameRate + 0.000001) / settings.FrameRate;
            if (Math.Abs(quantized - Time) < 0.000001) return;
            Time = quantized;
            InvalidateVisual();
        }

        private static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
        private static double Smooth(double v) { v = Clamp(v); return v * v * (3 - 2 * v); }
        private static double Ease(double v) { return 1 - Math.Pow(1 - Clamp(v), 3); }
        private static double Phase(double v, double start, double end) { return Smooth((v - start) / (end - start)); }
        private static double Pulse(double v, double center, double width)
        {
            return Smooth(1 - Math.Abs(v - center) / width);
        }
        private static double Hash(int value)
        {
            double n = Math.Sin(value * 91.733 + 17.17) * 43758.5453;
            return n - Math.Floor(n);
        }
        private static Brush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static Brush Alpha(double a, byte r, byte g, byte b) { return new SolidColorBrush(Color.FromArgb((byte)(Clamp(a) * 255), r, g, b)); }

        private static Geometry TextGeometry(string text, Typeface face, double size, double x, double y, double tracking)
        {
            var group = new GeometryGroup();
            foreach (char letter in text)
            {
                var formatted = new FormattedText(letter.ToString(), CultureInfo.GetCultureInfo("zh-CN"),
                    FlowDirection.LeftToRight, face, size, Brushes.White, 1.0);
                group.Children.Add(formatted.BuildGeometry(new Point(x, y)));
                x += formatted.WidthIncludingTrailingWhitespace + tracking;
            }
            group.Freeze();
            return group;
        }

        private void Text(DrawingContext dc, string value, double x, double y, double size, string color, bool code)
        {
            dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                code ? mono : normal, size, Brush(color), 1.0), new Point(x, y));
        }

        private static LinearGradientBrush Gradient(string a, string b, string c)
        {
            var gradient = new LinearGradientBrush();
            gradient.StartPoint = new Point(0, 0);
            gradient.EndPoint = new Point(1, 1);
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(a), 0));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(b), 0.52));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), 1));
            return gradient;
        }

        private void DrawBackdrop(DrawingContext dc, double p)
        {
            var night = new LinearGradientBrush();
            night.StartPoint = new Point(0, 0);
            night.EndPoint = new Point(1, 1);
            night.GradientStops.Add(new GradientStop(Color.FromRgb(1, 5, 13), 0));
            night.GradientStops.Add(new GradientStop(Color.FromRgb(4, 24, 38), 0.48));
            night.GradientStops.Add(new GradientStop(Color.FromRgb(22, 8, 29), 1));
            dc.DrawRectangle(night, null, new Rect(0, 0, DesignWidth, DesignHeight));

            double energy = reducedMotion ? 0.48 : Phase(p, 0.01, 0.22);
            for (int ribbon = 0; ribbon < 3; ribbon++)
            {
                double drift = reducedMotion ? 0 : Math.Sin(Time * (0.34 + ribbon * 0.08) + ribbon * 2.1) * 42;
                double baseY = 115 + ribbon * 155 + drift;
                Geometry wave = BuildPath(delegate(StreamGeometryContext g)
                {
                    g.BeginFigure(new Point(-80, baseY), true, true);
                    g.BezierTo(new Point(250, baseY - 130), new Point(520, baseY + 120), new Point(810, baseY - 22), true, false);
                    g.BezierTo(new Point(1010, baseY - 125), new Point(1260, baseY + 45), new Point(1380, baseY - 65), true, false);
                    g.LineTo(new Point(1380, baseY + 86), true, false);
                    g.BezierTo(new Point(1040, baseY + 190), new Point(650, baseY + 45), new Point(-80, baseY + 115), true, false);
                });
                string color = ribbon == 0 ? "#215D9E" : ribbon == 1 ? "#286D70" : "#713E86";
                dc.PushOpacity((0.10 + ribbon * 0.025) * energy);
                dc.DrawGeometry(Brush(color), null, wave);
                dc.Pop();
            }

            DrawParticles(dc, p, false);
        }

        private void DrawParticles(DrawingContext dc, double p, bool foreground)
        {
            int count = foreground ? 20 : 46;
            double start = foreground ? 0.25 : 0.03;
            double visible = reducedMotion ? 0.55 : Phase(p, start, start + 0.16);
            if (visible <= 0) return;
            for (int i = 0; i < count; i++)
            {
                double seedX = Hash(i * 7 + (foreground ? 211 : 5));
                double seedY = Hash(i * 13 + (foreground ? 401 : 23));
                double speed = 9 + Hash(i * 17 + 9) * (foreground ? 32 : 18);
                double x = seedX * DesignWidth + Math.Sin(Time * 0.5 + i) * (foreground ? 15 : 5);
                double y = (seedY * DesignHeight - Time * speed) % (DesignHeight + 60);
                if (y < -30) y += DesignHeight + 60;
                double size = (foreground ? 1.6 : 0.8) + Hash(i * 29 + 31) * (foreground ? 3.2 : 1.9);
                double twinkle = 0.42 + 0.58 * Math.Abs(Math.Sin(Time * (1.2 + Hash(i) * 1.8) + i));
                Color c = i % 5 == 0 ? Color.FromRgb(255, 153, 205) : Color.FromRgb(160, 239, 255);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(visible * twinkle * (foreground ? 145 : 90)), c.R, c.G, c.B)),
                    null, new Point(x, y), size, size);
            }
        }

        private Rect ImageRect(double p)
        {
            double breath = reducedMotion ? 0 : Math.Sin(Time * 1.05) * 2.8;
            double zoom = 1.045 + Phase(p, 0.19, 0.88) * 0.035;
            double w = DesignWidth * zoom;
            double h = DesignHeight * zoom;
            return new Rect(-48 - (w - DesignWidth) * 0.28, -16 - (h - DesignHeight) * 0.42 + breath, w, h);
        }

        private void DrawReference(DrawingContext dc, double p)
        {
            double reveal = reducedMotion ? 1 : Phase(p, 0.18, 0.43);
            if (reveal <= 0.001) return;
            Rect imageRect = ImageRect(p);
            var mask = new GeometryGroup { FillRule = FillRule.Nonzero };
            Point origin = new Point(596, 326);
            mask.Children.Add(new EllipseGeometry(origin, 34 + 910 * reveal, 26 + 610 * reveal));
            for (int i = 0; i < 9; i++)
            {
                double local = Clamp(reveal * 1.32 - i * 0.055);
                double width = 22 + local * 98;
                double x = 84 + i * 132 + Math.Sin(i * 1.7) * 34;
                mask.Children.Add(new RectangleGeometry(new Rect(x, 360 - 410 * local, width, 820 * local), 4, 4));
            }

            dc.PushClip(mask);
            dc.PushOpacity(0.08 + 0.90 * reveal);
            dc.DrawImage(reference, imageRect);
            dc.Pop();
            dc.Pop();

            if (!reducedMotion && reveal > 0.55) DrawHairMotion(dc, imageRect, reveal);

            var grade = new LinearGradientBrush();
            grade.StartPoint = new Point(0, 0);
            grade.EndPoint = new Point(1, 0);
            grade.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(30 * reveal), 16, 70, 89), 0));
            grade.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(55 * reveal), 11, 36, 58), 0.48));
            grade.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(212 * reveal), 3, 10, 20), 1));
            dc.DrawRectangle(grade, null, new Rect(0, 0, DesignWidth, DesignHeight));

            double birthFlash = Math.Sin(Phase(p, 0.30, 0.46) * Math.PI);
            if (birthFlash > 0)
            {
                var halo = new RadialGradientBrush();
                halo.Center = new Point(0.47, 0.47);
                halo.GradientOrigin = halo.Center;
                halo.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(110 * birthFlash), 225, 255, 255), 0));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(42 * birthFlash), 83, 219, 255), 0.24));
                halo.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                dc.DrawEllipse(halo, null, origin, 245, 245);
            }
        }

        private void DrawHairMotion(DrawingContext dc, Rect imageRect, double reveal)
        {
            double amount = Smooth((reveal - 0.55) / 0.45);
            double swayA = Math.Sin(Time * 1.28) * 4.8 * amount;
            double swayB = Math.Sin(Time * 0.92 + 1.4) * 6.2 * amount;
            double lift = Math.Sin(Time * 1.75 + 0.6) * 2.3 * amount;

            dc.PushOpacity(0.90 * amount);
            dc.PushClip(new EllipseGeometry(new Rect(30, -80, 620, 570)));
            dc.PushTransform(new RotateTransform(swayA * 0.085, 355, 105));
            dc.DrawImage(reference, new Rect(imageRect.X + swayA, imageRect.Y + lift, imageRect.Width, imageRect.Height));
            dc.Pop();
            dc.Pop();

            dc.PushClip(new RectangleGeometry(new Rect(655, 75, 220, 565), 80, 80));
            dc.PushTransform(new RotateTransform(swayB * 0.10, 724, 178));
            dc.DrawImage(reference, new Rect(imageRect.X + swayB, imageRect.Y + lift * 0.5, imageRect.Width, imageRect.Height));
            dc.Pop();
            dc.Pop();

            dc.PushClip(new EllipseGeometry(new Rect(118, 468, 720, 350)));
            dc.DrawImage(reference, new Rect(imageRect.X, imageRect.Y + Math.Sin(Time * 1.18) * 2.2 * amount, imageRect.Width, imageRect.Height));
            dc.Pop();
            dc.Pop();
        }

        private static StreamGeometry BuildPath(Action<StreamGeometryContext> draw)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open()) draw(context);
            geometry.Freeze();
            return geometry;
        }

        private void DrawLinePhase(DrawingContext dc, double p)
        {
            double appear = Phase(p, 0.035, 0.13) * (1 - Phase(p, 0.31, 0.43));
            if (appear <= 0) return;
            double sweep = Phase(p, 0.06, 0.31);
            dc.PushOpacity(appear);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 740 * sweep + 70, DesignHeight)));
            Pen glow = new Pen(Alpha(0.24, 68, 211, 255), 10.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            Pen line = new Pen(Alpha(0.92, 231, 253, 255), 1.35) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            DrawRemContours(dc, glow);
            DrawRemContours(dc, line);
            dc.Pop();
            double headX = 90 + 630 * sweep;
            Point scanner = new Point(headX, 286 + Math.Sin(Time * 3.1) * 28);
            dc.DrawEllipse(Alpha(appear, 255, 255, 255), null, scanner, 4.5, 4.5);
            dc.DrawEllipse(null, new Pen(Alpha(appear * 0.55, 104, 226, 255), 1.2), scanner, 20, 20);
            double burst = Math.Sin(Phase(p, 0.12, 0.27) * Math.PI) * appear;
            for (int i = 0; i < 12 && burst > 0; i++)
            {
                double angle = i * Math.PI * 2 / 12.0 + Hash(i) * 0.24;
                double distance = 52 + Hash(i + 20) * 118 * burst;
                Point a = new Point(595 + Math.Cos(angle) * distance, 326 + Math.Sin(angle) * distance);
                Point b = new Point(a.X + Math.Cos(angle + 2.1) * (7 + Hash(i + 40) * 14), a.Y + Math.Sin(angle + 2.1) * (7 + Hash(i + 40) * 14));
                Point c = new Point(a.X + Math.Cos(angle - 2.0) * (5 + Hash(i + 60) * 10), a.Y + Math.Sin(angle - 2.0) * (5 + Hash(i + 60) * 10));
                Geometry shard = BuildPath(delegate(StreamGeometryContext g)
                {
                    g.BeginFigure(a, true, true);
                    g.LineTo(b, true, false);
                    g.LineTo(c, true, false);
                });
                dc.DrawGeometry(Alpha(burst * 0.14, 139, 229, 255), new Pen(Alpha(burst * 0.58, 211, 249, 255), 0.8), shard);
            }
            dc.Pop();
        }

        private void DrawRemContours(DrawingContext dc, Pen pen)
        {
            dc.DrawGeometry(null, pen, BuildPath(delegate(StreamGeometryContext g)
            {
                g.BeginFigure(new Point(218, 68), false, false);
                g.BezierTo(new Point(120, 130), new Point(52, 248), new Point(34, 356), true, false);
                g.BezierTo(new Point(120, 331), new Point(169, 237), new Point(250, 186), true, false);
                g.BezierTo(new Point(332, 114), new Point(474, 89), new Point(585, 130), true, false);
            }));
            dc.DrawGeometry(null, pen, BuildPath(delegate(StreamGeometryContext g)
            {
                g.BeginFigure(new Point(392, 90), false, false);
                g.BezierTo(new Point(352, 178), new Point(328, 276), new Point(344, 408), true, false);
                g.BezierTo(new Point(288, 421), new Point(222, 413), new Point(160, 380), true, false);
            }));
            dc.DrawGeometry(null, pen, BuildPath(delegate(StreamGeometryContext g)
            {
                g.BeginFigure(new Point(492, 101), false, false);
                g.BezierTo(new Point(552, 178), new Point(548, 287), new Point(535, 400), true, false);
                g.BezierTo(new Point(596, 389), new Point(668, 349), new Point(710, 270), true, false);
            }));
            dc.DrawGeometry(null, pen, BuildPath(delegate(StreamGeometryContext g)
            {
                g.BeginFigure(new Point(336, 304), false, false);
                g.BezierTo(new Point(396, 262), new Point(484, 250), new Point(548, 296), true, false);
                g.BezierTo(new Point(520, 345), new Point(438, 354), new Point(368, 326), true, false);
            }));
            dc.DrawGeometry(null, pen, BuildPath(delegate(StreamGeometryContext g)
            {
                g.BeginFigure(new Point(245, 531), false, false);
                g.BezierTo(new Point(335, 670), new Point(531, 677), new Point(651, 548), true, false);
                g.BezierTo(new Point(596, 620), new Point(478, 690), new Point(340, 646), true, false);
            }));
            dc.DrawGeometry(null, pen, BuildPath(delegate(StreamGeometryContext g)
            {
                g.BeginFigure(new Point(568, 141), false, false);
                g.LineTo(new Point(660, 196), true, false);
                g.LineTo(new Point(570, 247), true, false);
                g.LineTo(new Point(653, 305), true, false);
            }));
        }

        private void DrawEyeGlint(DrawingContext dc, double p)
        {
            double awake = reducedMotion ? 1 : Phase(p, 0.37, 0.54);
            if (awake <= 0) return;
            double gazeX = reducedMotion ? 0 : Math.Sin(Time * 0.72) * 2.8;
            double gazeY = reducedMotion ? 0 : Math.Sin(Time * 0.51 + 1.3) * 1.5;
            Point eye = new Point(595 + gazeX, 326 + gazeY);

            double flash = Math.Sin(Phase(p, 0.40, 0.55) * Math.PI);
            dc.PushOpacity(0.18 + 0.66 * awake);
            var iris = new RadialGradientBrush();
            iris.GradientStops.Add(new GradientStop(Color.FromArgb(35, 255, 255, 255), 0));
            iris.GradientStops.Add(new GradientStop(Color.FromArgb(72, 75, 227, 255), 0.28));
            iris.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.DrawEllipse(iris, null, eye, 12 * awake, 16 * awake);
            Point glint = new Point(eye.X - 7 + Math.Sin(Time * 1.4) * 2, eye.Y - 12);
            dc.DrawEllipse(Alpha(0.88, 255, 255, 255), null, glint, 2.8 * awake, 2.8 * awake);
            dc.Pop();
            if (flash > 0)
            {
                dc.PushOpacity(0.42 * flash);
                dc.DrawLine(new Pen(Brush("#FFFFFF"), 2.2), new Point(eye.X - 88, eye.Y), new Point(eye.X + 92, eye.Y));
                dc.DrawLine(new Pen(Brush("#FFFFFF"), 1.2), new Point(eye.X, eye.Y - 76), new Point(eye.X, eye.Y + 78));
                dc.Pop();
            }
        }

        private void DrawInterface(DrawingContext dc, double p)
        {
            var shade = new LinearGradientBrush();
            shade.StartPoint = new Point(0, 0);
            shade.EndPoint = new Point(1, 0);
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(35, 5, 17, 27), 0.48));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(232, 3, 9, 18), 1));
            dc.DrawRectangle(shade, null, new Rect(0, 0, DesignWidth, DesignHeight));

            double title = reducedMotion ? 1 : Phase(p, 0.50, 0.66);
            dc.PushOpacity(title);
            dc.PushClip(new RectangleGeometry(new Rect(770, 190, 440 * title, 160)));
            dc.DrawGeometry(null, new Pen(Alpha(0.20, 42, 220, 250), 11), codexGlyph);
            dc.DrawGeometry(null, new Pen(Alpha(0.42, 255, 255, 255), 1.2), codexGlyph);
            dc.DrawGeometry(Gradient("#EFFFFF", "#B7F4FF", "#E47FB5"), null, codexGlyph);

            double glintX = 760 + Phase(p, 0.55, 0.78) * 520;
            var glint = new LinearGradientBrush();
            glint.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(130, 255, 255, 255), 0.5));
            glint.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.PushClip(codexGlyph);
            dc.DrawRectangle(glint, null, new Rect(glintX, 204, 60, 150));
            dc.Pop();
            dc.Pop();
            dc.Pop();

            double message = reducedMotion ? 1 : Phase(p, 0.62, 0.74);
            dc.PushOpacity(message);
            Brush textFill = Gradient("#F6FFFF", "#9FEAFF", "#F4A8CA");
            dc.DrawGeometry(textFill, null, cnGlyph);
            dc.PushOpacity(0.72);
            dc.DrawGeometry(textFill, null, enGlyph);
            dc.Pop();
            dc.DrawLine(new Pen(Alpha(0.65, 186, 244, 255), 1.5), new Point(805, 392), new Point(1128, 392));
            dc.Pop();

            double hud = reducedMotion ? 1 : Phase(p, 0.02, 0.09);
            dc.PushOpacity(hud);
            Text(dc, "Codex", 40, 32, 18, "#E6FFFF", true);
            Text(dc, "REM / CRYSTAL WAKE", 40, 58, 10, "#BDEEF5", true);
            Text(dc, "01  ICE TRACE", 41, 136, 10, p < 0.31 ? "#F5FFFF" : "#719AA5", true);
            Text(dc, "02  MATERIALIZE", 41, 154, 10, p >= 0.18 && p < 0.52 ? "#F5FFFF" : "#719AA5", true);
            Text(dc, "03  EYE SYNC", 41, 172, 10, p >= 0.37 && p < 0.68 ? "#F5FFFF" : "#719AA5", true);
            Text(dc, "04  CODEX READY", 41, 190, 10, p >= 0.62 ? "#F5FFFF" : "#719AA5", true);
            dc.Pop();

            double progress = reducedMotion ? 1 : p;
            dc.DrawLine(new Pen(Alpha(0.22, 218, 255, 255), 1), new Point(805, 604), new Point(1180, 604));
            dc.DrawLine(new Pen(Alpha(0.78, 255, 160, 207), 2.2), new Point(805, 604), new Point(805 + 375 * progress, 604));
            string status = Launching ? "正在打开 Codex…" :
                p < 0.18 ? "冰晶正在聚合…" :
                p < 0.42 ? "正在描绘雷姆轮廓…" :
                p < 0.62 ? "正在同步眼神与呼吸…" :
                p < 0.88 ? "Codex 启动协议就绪…" : "准备出发。";
            Text(dc, status, 805, 626, 14, "#DFF9FF", false);
            Text(dc, ((int)(progress * 100)).ToString("D2") + "%", 1194, 594, 10, "#BDEEF5", true);
            Text(dc, "ESC / ENTER / SPACE 跳过", 40, 672, 11, "#BDEEF5", false);
        }

        private void DrawFinale(DrawingContext dc, double p)
        {
            double ready = Phase(p, 0.84, 0.92);
            if (ready > 0)
            {
                Point center = new Point(986, 306);
                double radius = 90 + ready * 420;
                dc.DrawEllipse(null, new Pen(Alpha((1 - ready) * 0.62, 164, 242, 255), 1.4), center, radius, radius);
                dc.DrawEllipse(null, new Pen(Alpha((1 - ready) * 0.38, 255, 151, 205), 2.0), center, radius * 0.74, radius * 0.74);
            }
            double close = Phase(p, 0.965, 1.0);
            if (close > 0)
                dc.DrawRectangle(Alpha(close, 1, 4, 10), null, new Rect(0, 0, DesignWidth, DesignHeight));
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double p = reducedMotion ? 0.82 : Clamp(Time / settings.Duration);
            var screen = new Rect(0, 0, DesignWidth, DesignHeight);
            dc.PushClip(new RectangleGeometry(screen, 10, 10));
            DrawBackdrop(dc, p);
            DrawLinePhase(dc, p);
            DrawReference(dc, p);
            DrawEyeGlint(dc, p);
            DrawParticles(dc, p, true);
            DrawInterface(dc, p);
            DrawFinale(dc, p);
            for (int y = 0; y < DesignHeight; y += 3)
                dc.DrawLine(new Pen(Alpha(0.018, 135, 220, 235), 0.55), new Point(0, y), new Point(DesignWidth, y));
            dc.Pop();
        }
    }

    public sealed class Splash : Window
    {
        private readonly RemScene scene;
        private readonly Settings settings;
        private readonly bool preview;
        private readonly Stopwatch clock = new Stopwatch();
        private bool done, closed;

        public Splash(RemScene graphic, Settings config, bool onlyPreview)
        {
            scene = graphic;
            settings = config;
            preview = onlyPreview;
            Title = "雷姆 · Codex 启动";
            Width = Math.Min(RemScene.DesignWidth, SystemParameters.WorkArea.Width * 0.9);
            Height = Width * RemScene.DesignHeight / RemScene.DesignWidth;
            if (Height > SystemParameters.WorkArea.Height * 0.9)
            {
                Height = SystemParameters.WorkArea.Height * 0.9;
                Width = Height * RemScene.DesignWidth / RemScene.DesignHeight;
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Content = new System.Windows.Controls.Viewbox { Child = scene, Stretch = Stretch.Uniform };
            ContentRendered += delegate { clock.Start(); CompositionTarget.Rendering += Tick; Activate(); Focus(); };
            Closed += delegate { closed = true; done = true; CompositionTarget.Rendering -= Tick; clock.Stop(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    Finish();
                }
            };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                Point p = e.GetPosition(scene);
                if (p.Y < 76) try { DragMove(); } catch (InvalidOperationException) { }
            };
        }

        private void Tick(object sender, EventArgs e)
        {
            scene.SetTime(clock.Elapsed.TotalSeconds);
            if (clock.Elapsed.TotalSeconds >= settings.Duration) Finish();
        }

        private void Finish()
        {
            if (done) return;
            done = true;
            CompositionTarget.Rendering -= Tick;
            if (preview) { Close(); return; }
            scene.Launching = true;
            scene.SetTime(settings.Duration - 0.12);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (closed) return;
                try { AppActivation.Open(settings); Close(); }
                catch (Exception error)
                {
                    Topmost = false;
                    MessageBox.Show(this, "无法打开 Codex。请检查 launcher.config.xml。\n\n" + error.Message,
                        "雷姆 Codex 启动器", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            }), DispatcherPriority.Background);
        }
    }

    public static class Program
    {
        private static RenderTargetBitmap RenderFrame(RemScene scene, double time)
        {
            scene.SetTime(time);
            scene.Measure(new Size(RemScene.DesignWidth, RemScene.DesignHeight));
            scene.Arrange(new Rect(0, 0, RemScene.DesignWidth, RemScene.DesignHeight));
            scene.UpdateLayout();
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
                dc.DrawRectangle(new VisualBrush(scene) { Stretch = Stretch.Fill }, null,
                    new Rect(0, 0, RemScene.ExportWidth, RemScene.ExportHeight));
            var bitmap = new RenderTargetBitmap(RemScene.ExportWidth, RemScene.ExportHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private static void SaveFrame(RemScene scene, double time, string path)
        {
            RenderTargetBitmap bitmap = RenderFrame(scene, time);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) encoder.Save(file);
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        private static void RenderVideo(RemScene scene, Settings config, string outputPath, string ffmpegPath)
        {
            if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("找不到 FFmpeg。", ffmpegPath);
            outputPath = Path.GetFullPath(outputPath);
            string parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            int frames = (int)Math.Ceiling(config.Duration * config.FrameRate);
            string arguments = "-hide_banner -loglevel warning -y -f rawvideo -pixel_format bgra -video_size " +
                RemScene.ExportWidth + "x" + RemScene.ExportHeight + " -framerate " + config.FrameRate +
                " -i pipe:0 -frames:v " + frames + " -c:v libx264 -preset fast -crf 17 -pix_fmt yuv420p -r " +
                config.FrameRate + " -movflags +faststart " + Quote(outputPath);
            var start = new ProcessStartInfo(ffmpegPath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            using (Process encoder = Process.Start(start))
            {
                if (encoder == null) throw new InvalidOperationException("FFmpeg 无法启动。");
                int stride = RemScene.ExportWidth * 4;
                byte[] pixels = new byte[stride * RemScene.ExportHeight];
                string progress = Path.Combine(parent ?? AppDomain.CurrentDomain.BaseDirectory, "render-progress.txt");
                try
                {
                    for (int i = 0; i < frames; i++)
                    {
                        RenderTargetBitmap bitmap = RenderFrame(scene, i / (double)config.FrameRate);
                        bitmap.CopyPixels(pixels, stride, 0);
                        encoder.StandardInput.BaseStream.Write(pixels, 0, pixels.Length);
                        if (i % config.FrameRate == 0)
                            File.WriteAllText(progress, ((int)(i * 100.0 / frames)).ToString(CultureInfo.InvariantCulture));
                    }
                    encoder.StandardInput.Close();
                    encoder.WaitForExit();
                    if (encoder.ExitCode != 0) throw new InvalidOperationException("FFmpeg 视频编码失败，退出码：" + encoder.ExitCode);
                    File.WriteAllText(progress, "100");
                }
                catch
                {
                    try { if (!encoder.HasExited) encoder.Kill(); } catch { }
                    throw;
                }
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                Settings config = Settings.Load(root);
                bool preview = Array.IndexOf(args, "--preview") >= 0;
                bool reduced = Array.IndexOf(args, "--reduced-motion") >= 0 ||
                    (config.RespectReducedMotion && !SystemParameters.ClientAreaAnimation);
                int render = Array.IndexOf(args, "--render");
                int renderVideo = Array.IndexOf(args, "--render-video");
                bool selfTest = Array.IndexOf(args, "--self-test") >= 0;
                if (Array.IndexOf(args, "--activation-test") >= 0)
                {
                    uint pid = AppActivation.Open(config);
                    Directory.CreateDirectory(Path.Combine(root, "output"));
                    File.WriteAllText(Path.Combine(root, "output", "activation-test.txt"), "PASS: Codex activation accepted. PID: " + pid);
                    return 0;
                }
                if (renderVideo >= 0)
                {
                    if (renderVideo + 2 >= args.Length) throw new ArgumentException("--render-video 后需要指定 MP4 输出路径和 FFmpeg 路径。");
                    var videoScene = new RemScene(root, config, reduced);
                    RenderVideo(videoScene, config, args[renderVideo + 1], args[renderVideo + 2]);
                    return 0;
                }
                if (render >= 0 || selfTest)
                {
                    if (render >= 0 && render + 1 >= args.Length) throw new ArgumentException("--render 后需要指定输出目录。");
                    string folder = selfTest ? Path.Combine(root, "output", "self-test") : Path.GetFullPath(args[render + 1]);
                    Directory.CreateDirectory(folder);
                    var scene = new RemScene(root, config, reduced);
                    if (selfTest)
                    {
                        SaveFrame(scene, config.Duration * 0.07, Path.Combine(folder, "intro.png"));
                        SaveFrame(scene, config.Duration * 0.18, Path.Combine(folder, "line-trace.png"));
                        SaveFrame(scene, config.Duration * 0.34, Path.Combine(folder, "materialize.png"));
                        SaveFrame(scene, config.Duration * 0.50, Path.Combine(folder, "wake.png"));
                        SaveFrame(scene, config.Duration * 0.555, Path.Combine(folder, "gaze-shift.png"));
                        SaveFrame(scene, config.Duration * 0.82, Path.Combine(folder, "ready.png"));
                        SaveFrame(scene, config.Duration * 0.94, Path.Combine(folder, "finale.png"));
                        File.WriteAllText(Path.Combine(folder, "result.txt"),
                            "PASS: Rem startup frames rendered. App activation was not executed.\nReference: " + config.ReferenceImage);
                    }
                    else
                    {
                        int frames = (int)Math.Ceiling(config.Duration * config.FrameRate);
                        for (int i = 0; i < frames; i++)
                            SaveFrame(scene, i / (double)config.FrameRate, Path.Combine(folder, "frame-" + i.ToString("D4") + ".png"));
                        SaveFrame(scene, config.Duration * 0.82, Path.Combine(folder, "poster.png"));
                    }
                    return 0;
                }
                bool acquired;
                using (var mutex = new Mutex(true, "Local\\RemCodexStartupAnimation", out acquired))
                {
                    if (!acquired) return 0;
                    new Application().Run(new Splash(new RemScene(root, config, reduced), config, preview));
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception error)
            {
                try
                {
                    string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "launcher-error.txt"), error.ToString());
                }
                catch { }
                if (Array.IndexOf(args, "--render") < 0 && Array.IndexOf(args, "--render-video") < 0 && Array.IndexOf(args, "--self-test") < 0 && Array.IndexOf(args, "--activation-test") < 0)
                    MessageBox.Show(error.Message, "雷姆 Codex 启动器", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
