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
        public double Duration = 7.2;
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
            result.Duration = double.Parse(value("durationSeconds", "7.2"), CultureInfo.InvariantCulture);
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
        public const int ExportWidth = 2560, ExportHeight = 1440;
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
            codexGlyph = TextGeometry("Codex", display, 116, 792, 222, 0.5);
            cnGlyph = TextGeometry("欢迎回来，主人", display, 29, 802, 360, 2.8);
            enGlyph = TextGeometry("REM STARTUP PROTOCOL", display, 14, 804, 404, 1.4);
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

        private void DrawReference(DrawingContext dc, double u)
        {
            double zoom = 1.035 + Smooth(u / 6.0) * 0.055;
            double w = DesignWidth * zoom;
            double h = DesignHeight * zoom;
            double driftX = -82 - Smooth(u / 8.0) * 34;
            double driftY = -18 - Math.Sin(u * 0.55) * 5;
            dc.PushOpacity(reducedMotion ? 0.82 : 0.58 + Smooth((u - 1.8) / 2.0) * 0.28);
            dc.DrawImage(reference, new Rect(driftX, driftY, w, h));
            dc.Pop();
        }

        private void DrawSoftLight(DrawingContext dc, double u)
        {
            var morning = new LinearGradientBrush();
            morning.StartPoint = new Point(0, 0);
            morning.EndPoint = new Point(1, 1);
            morning.GradientStops.Add(new GradientStop(Color.FromArgb(110, 255, 255, 236), 0));
            morning.GradientStops.Add(new GradientStop(Color.FromArgb(18, 147, 255, 243), 0.44));
            morning.GradientStops.Add(new GradientStop(Color.FromArgb(96, 20, 154, 54), 1));
            dc.DrawRectangle(morning, null, new Rect(0, 0, DesignWidth, DesignHeight));

            for (int i = 0; i < 7; i++)
            {
                double x = 80 + i * 192 + Math.Sin(u * 0.45 + i) * 13;
                var beam = new LinearGradientBrush();
                beam.StartPoint = new Point(0, 0);
                beam.EndPoint = new Point(0, 1);
                beam.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                beam.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(32 + i % 3 * 9), 255, 255, 255), 0.42));
                beam.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                dc.PushTransform(new RotateTransform(-11 + i * 1.8, x, 360));
                dc.DrawRectangle(beam, null, new Rect(x, -80, 74, 900));
                dc.Pop();
            }
        }

        private static StreamGeometry BuildPath(Action<StreamGeometryContext> draw)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open()) draw(context);
            geometry.Freeze();
            return geometry;
        }

        private void DrawLinePhase(DrawingContext dc, double u)
        {
            double appear = Ease(u / 2.4) * (1 - Smooth((u - 2.3) / 1.2));
            if (appear <= 0) return;
            double sweep = Clamp(u / 2.35);
            dc.PushOpacity(appear);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 740 * sweep + 70, DesignHeight)));
            Pen glow = new Pen(Alpha(0.20, 125, 239, 255), 8.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            Pen line = new Pen(Alpha(0.86, 235, 255, 255), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            DrawRemContours(dc, glow);
            DrawRemContours(dc, line);
            dc.Pop();
            double headX = 90 + 630 * sweep;
            dc.DrawEllipse(Alpha(appear, 255, 255, 255), null, new Point(headX, 286 + Math.Sin(u * 3.1) * 28), 4.5, 4.5);
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

        private void DrawEyeGlint(DrawingContext dc, double u)
        {
            double awake = reducedMotion ? 1 : Smooth((u - 3.05) / 1.35);
            double flash = Math.Sin(Math.Min(1, awake) * Math.PI);
            Point eye = new Point(595, 326);
            dc.PushOpacity(0.18 + 0.62 * awake);
            var iris = new RadialGradientBrush();
            iris.GradientStops.Add(new GradientStop(Color.FromArgb(240, 255, 255, 255), 0));
            iris.GradientStops.Add(new GradientStop(Color.FromArgb(210, 66, 219, 247), 0.26));
            iris.GradientStops.Add(new GradientStop(Color.FromArgb(190, 44, 117, 190), 1));
            dc.DrawEllipse(iris, new Pen(Alpha(0.45, 255, 255, 255), 1.2), eye, 25 * awake, 33 * awake);
            dc.DrawEllipse(Alpha(0.85, 255, 255, 255), null, new Point(eye.X - 8, eye.Y - 13), 5.2 * awake, 5.2 * awake);
            dc.Pop();
            if (flash > 0)
            {
                dc.PushOpacity(0.35 * flash);
                dc.DrawLine(new Pen(Brush("#FFFFFF"), 2.6), new Point(eye.X - 62, eye.Y), new Point(eye.X + 65, eye.Y));
                dc.DrawLine(new Pen(Brush("#FFFFFF"), 1.4), new Point(eye.X, eye.Y - 56), new Point(eye.X, eye.Y + 58));
                dc.Pop();
            }
        }

        private void DrawInterface(DrawingContext dc, double u)
        {
            var shade = new LinearGradientBrush();
            shade.StartPoint = new Point(0, 0);
            shade.EndPoint = new Point(1, 0);
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(45, 255, 255, 255), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(155, 5, 17, 27), 0.58));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(230, 5, 17, 27), 1));
            dc.DrawRectangle(shade, null, new Rect(0, 0, DesignWidth, DesignHeight));

            double title = Smooth((u - 2.85) / 0.95);
            dc.PushOpacity(title);
            dc.DrawGeometry(null, new Pen(Alpha(0.16, 42, 220, 250), 9), codexGlyph);
            dc.DrawGeometry(null, new Pen(Alpha(0.42, 255, 255, 255), 1.2), codexGlyph);
            dc.DrawGeometry(Gradient("#EFFFFF", "#B7F4FF", "#E47FB5"), null, codexGlyph);

            double glintX = 770 + (u - 3.2) * 205;
            var glint = new LinearGradientBrush();
            glint.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.5));
            glint.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.PushClip(codexGlyph);
            dc.DrawRectangle(glint, null, new Rect(glintX, 204, 60, 150));
            dc.Pop();
            dc.Pop();

            double message = Smooth((u - 3.65) / 0.85);
            dc.PushOpacity(message);
            Brush textFill = Gradient("#F6FFFF", "#9FEAFF", "#F4A8CA");
            dc.DrawGeometry(textFill, null, cnGlyph);
            dc.PushOpacity(0.72);
            dc.DrawGeometry(textFill, null, enGlyph);
            dc.Pop();
            dc.DrawLine(new Pen(Alpha(0.65, 186, 244, 255), 1.5), new Point(805, 392), new Point(1128, 392));
            dc.Pop();

            Text(dc, "Codex", 40, 32, 18, "#E6FFFF", true);
            Text(dc, "REM / SOFT LIGHT BOOT", 40, 58, 10, "#BDEEF5", true);
            Text(dc, "01  LINE TRACE", 41, 136, 10, u < 2.4 ? "#F5FFFF" : "#9AC6CD", true);
            Text(dc, "02  MATERIALIZE", 41, 154, 10, u >= 2.4 && u < 3.25 ? "#F5FFFF" : "#9AC6CD", true);
            Text(dc, "03  WAKE", 41, 172, 10, u >= 3.25 ? "#F5FFFF" : "#9AC6CD", true);

            double progress = reducedMotion ? 1 : Clamp(Time / settings.Duration);
            dc.DrawLine(new Pen(Alpha(0.22, 218, 255, 255), 1), new Point(805, 604), new Point(1180, 604));
            dc.DrawLine(new Pen(Alpha(0.78, 255, 160, 207), 2.2), new Point(805, 604), new Point(805 + 375 * progress, 604));
            string status = Launching ? "正在打开 Codex…" :
                u < 2.4 ? "正在描绘雷姆轮廓…" :
                u < 3.25 ? "正在让柔光显影…" :
                u < 4.65 ? "雷姆正在醒来…" : "准备出发。";
            Text(dc, status, 805, 626, 14, "#DFF9FF", false);
            Text(dc, ((int)(progress * 100)).ToString("D2") + "%", 1194, 594, 10, "#BDEEF5", true);
            Text(dc, "ESC / ENTER / SPACE 跳过", 40, 672, 11, "#BDEEF5", false);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double u = reducedMotion ? 5.2 : Time * 8 / settings.Duration;
            var screen = new Rect(0, 0, DesignWidth, DesignHeight);
            dc.PushClip(new RectangleGeometry(screen, 10, 10));
            dc.DrawRectangle(Brush("#F7FFF6"), null, screen);
            DrawReference(dc, u);
            DrawSoftLight(dc, u);
            DrawLinePhase(dc, u);
            DrawEyeGlint(dc, u);
            DrawInterface(dc, u);
            for (int y = 0; y < DesignHeight; y += 3)
                dc.DrawLine(new Pen(Alpha(0.012, 10, 28, 40), 0.6), new Point(0, y), new Point(DesignWidth, y));
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
        private static void SaveFrame(RemScene scene, double time, string path)
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
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) encoder.Save(file);
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
                bool selfTest = Array.IndexOf(args, "--self-test") >= 0;
                if (Array.IndexOf(args, "--activation-test") >= 0)
                {
                    uint pid = AppActivation.Open(config);
                    Directory.CreateDirectory(Path.Combine(root, "output"));
                    File.WriteAllText(Path.Combine(root, "output", "activation-test.txt"), "PASS: Codex activation accepted. PID: " + pid);
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
                        SaveFrame(scene, config.Duration * 0.18, Path.Combine(folder, "line-trace.png"));
                        SaveFrame(scene, config.Duration * 0.50, Path.Combine(folder, "wake.png"));
                        SaveFrame(scene, config.Duration * 0.82, Path.Combine(folder, "ready.png"));
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
                if (Array.IndexOf(args, "--render") < 0 && Array.IndexOf(args, "--self-test") < 0 && Array.IndexOf(args, "--activation-test") < 0)
                    MessageBox.Show(error.Message, "雷姆 Codex 启动器", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
