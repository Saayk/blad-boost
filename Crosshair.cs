// Crosshair - punto de mira superpuesto de GameBoost (BLAD Studios)
//
// Dibuja una ventana transparente, siempre encima y que deja pasar los clics, centrada en el
// monitor elegido. No inyecta nada en el juego ni lee su memoria: es una ventana normal de Windows.
// Se ve sobre juegos en ventana sin bordes y en pantalla completa (Windows 10/11).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace GameBoost
{
    // ------------------------------------------------------------------ modelo
    // Todo lo que define el aspecto de la mira. Es lo que se comparte con el "código" de crosshair.
    public class CrosshairLook
    {
        public int Style, Opacity = 100, Size = 10, Thickness = 2, Gap = 4, Rotation, OutlineWidth = 1, DotSize = 3, ImageSize = 32;
        public string Color = "#00FF66", DotColor = "#FF3B3B", OutlineColor = "#000000", Image = "";
        public bool Outline = true, Dot = true, DotOwnColor, Glow;

        public CrosshairLook Clone() { return (CrosshairLook)MemberwiseClone(); }

        public void CopyFrom(CrosshairLook o)
        {
            foreach (var f in typeof(CrosshairLook).GetFields()) f.SetValue(this, f.GetValue(o));
        }

        static int Cl(int v, int min, int max) { return v < min ? min : v > max ? max : v; }

        public static bool TryColor(string hex, out Color c)
        {
            c = Colors.White;
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || (hex.Length != 7 && hex.Length != 9)) return false;
            try { c = (Color)ColorConverter.ConvertFromString(hex); return true; }
            catch { return false; }
        }

        // Deja todos los valores dentro de rangos válidos (por si el archivo o el código vienen alterados).
        public void Clamp()
        {
            Style = Cl(Style, 0, CrosshairVisual.StyleNames.Length - 1);
            Opacity = Cl(Opacity, 10, 100); Size = Cl(Size, 1, 100); Thickness = Cl(Thickness, 1, 12); Gap = Cl(Gap, 0, 50);
            Rotation = Cl(Rotation, 0, 359); OutlineWidth = Cl(OutlineWidth, 1, 4); DotSize = Cl(DotSize, 1, 24); ImageSize = Cl(ImageSize, 8, 256);
            Color c;
            if (!TryColor(Color, out c)) Color = "#00FF66";
            if (!TryColor(DotColor, out c)) DotColor = "#FF3B3B";
            if (!TryColor(OutlineColor, out c)) OutlineColor = "#000000";
            if (Image == null) Image = "";
            if (Style == 9 && Image.Length == 0) Style = 0;
        }

        // Código para compartir: no incluye la ruta de la imagen (es personal).
        public string Export()
        {
            var c = Clone(); c.Image = ""; c.Clamp();
            string json = new JavaScriptSerializer().Serialize(c);
            return "BBCH1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        public static CrosshairLook Import(string code)
        {
            try
            {
                code = (code ?? "").Trim();
                if (!code.StartsWith("BBCH1:", StringComparison.Ordinal)) return null;
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(code.Substring(6)));
                var l = new JavaScriptSerializer().Deserialize<CrosshairLook>(json);
                if (l == null) return null;
                l.Image = ""; l.Clamp();
                return l;
            }
            catch { return null; }
        }
    }

    public class CrosshairSettings
    {
        public bool Enabled, OnlyInGame;
        public int Monitor = -1, OffsetX, OffsetY;
        public string Hotkey = "Ctrl+Alt+C";
        public CrosshairLook Look = new CrosshairLook();

        public void Fix()
        {
            if (Look == null) Look = new CrosshairLook();
            Look.Clamp();
            OffsetX = Math.Max(-400, Math.Min(400, OffsetX));
            OffsetY = Math.Max(-400, Math.Min(400, OffsetY));
            if (string.IsNullOrEmpty(Hotkey)) Hotkey = "None";
        }
    }

    public class CrosshairPreset
    {
        public string Name;
        public CrosshairLook Look;
    }

    static class CrosshairPresets
    {
        static CrosshairPreset P(string name, Action<CrosshairLook> f)
        {
            var l = new CrosshairLook(); f(l); l.Clamp();
            return new CrosshairPreset { Name = name, Look = l };
        }

        public static readonly CrosshairPreset[] All =
        {
            P("Clásico verde", l => { l.Style = 0; l.Color = "#00FF66"; l.Size = 10; l.Thickness = 2; l.Gap = 4; l.Dot = false; }),
            P("Punto rojo", l => { l.Style = 4; l.Dot = true; l.DotSize = 4; l.DotColor = "#FF3B3B"; l.DotOwnColor = true; }),
            P("Cian táctico", l => { l.Style = 1; l.Color = "#00E5FF"; l.Size = 8; l.Thickness = 2; l.Gap = 3; l.Dot = true; l.DotSize = 2; }),
            P("Círculo fino", l => { l.Style = 3; l.Color = "#FFFFFF"; l.Size = 12; l.Thickness = 1; l.Dot = true; l.DotSize = 2; }),
            P("Aspa magenta", l => { l.Style = 2; l.Color = "#FF2BD6"; l.Size = 8; l.Thickness = 2; l.Gap = 3; l.Dot = false; }),
            P("Diamante neón", l => { l.Style = 5; l.Color = "#B6FF00"; l.Size = 9; l.Thickness = 2; l.Glow = true; l.Dot = false; }),
            P("Esquinas blancas", l => { l.Style = 8; l.Color = "#FFFFFF"; l.Size = 14; l.Thickness = 2; l.Dot = true; l.DotSize = 2; }),
            P("Chevron naranja", l => { l.Style = 7; l.Color = "#FF9F1C"; l.Size = 10; l.Thickness = 3; l.Gap = 2; l.Dot = false; }),
            P("Francotirador", l => { l.Style = 0; l.Color = "#FFFFFF"; l.Size = 60; l.Thickness = 1; l.Gap = 8; l.Dot = true; l.DotSize = 2; l.Outline = false; l.Opacity = 85; })
        };
    }

    // ------------------------------------------------------------------ dibujo
    // Dibuja la mira en píxeles reales del monitor (no en unidades WPF), así 1 px de grosor son 1 px a cualquier escala de Windows.
    class CrosshairVisual : FrameworkElement
    {
        public static readonly string[] StyleNames =
            { "Cruz", "Cruz en T", "Aspa", "Círculo", "Punto", "Diamante", "Cuadrado", "Chevron", "Esquinas", "Imagen" };
        public static readonly string[] Palette =
            { "#00FF66", "#00E5FF", "#3B82F6", "#A855F7", "#FF2BD6", "#FF3B3B", "#FF9F1C", "#FFE600", "#B6FF00", "#FFFFFF" };

        public CrosshairLook Look = new CrosshairLook();
        BitmapSource img;
        string imgKey = "";

        public CrosshairVisual() { IsHitTestVisible = false; SnapsToDevicePixels = true; }

        // Tamaño (en píxeles, par) de la ventana que contiene la mira.
        public static int WindowPx(CrosshairLook l)
        {
            double e = (l.Size + l.Gap + l.Thickness) * 1.5;
            if (l.Style == 9) e = l.ImageSize * 0.75;
            e = Math.Max(e, l.DotSize) + l.OutlineWidth + 12;
            int px = 2 * (int)Math.Ceiling(e);
            return Math.Max(16, Math.Min(900, px));
        }

        static Brush Br(Color c, int alpha)
        {
            var b = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(c.A * alpha / 255), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        static Color Col(string hex, Color fallback) { Color c; return CrosshairLook.TryColor(hex, out c) ? c : fallback; }

        protected override void OnRender(DrawingContext dc)
        {
            var l = Look;
            if (l == null) return;
            var src = PresentationSource.FromVisual(this);
            double k = (src != null && src.CompositionTarget != null) ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
            if (k <= 0) k = 1;
            double cx = Math.Floor(ActualWidth * k / 2), cy = Math.Floor(ActualHeight * k / 2);

            dc.PushTransform(new ScaleTransform(1 / k, 1 / k));
            dc.PushOpacity(Math.Max(0.05, l.Opacity / 100.0));
            double ang = l.Rotation + (l.Style == 2 ? 45 : 0);
            if (ang != 0) dc.PushTransform(new RotateTransform(ang, cx, cy));

            if (l.Style == 9) DrawImage(dc, l, cx, cy);
            else
            {
                Color c = Col(l.Color, Colors.Lime);
                Color d = l.DotOwnColor ? Col(l.DotColor, Colors.Red) : c;
                Color o = Col(l.OutlineColor, Colors.Black);
                if (l.Glow)
                {
                    Layer(dc, l, cx, cy, 8, Br(c, 0x22), Br(d, 0x22));
                    Layer(dc, l, cx, cy, 4, Br(c, 0x3C), Br(d, 0x3C));
                }
                if (l.Outline) Layer(dc, l, cx, cy, l.OutlineWidth, Br(o, 255), Br(o, 255));
                Layer(dc, l, cx, cy, 0, Br(c, 255), Br(d, 255));
            }

            if (ang != 0) dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        static void R(DrawingContext dc, Brush b, double x, double y, double w, double h, double inf)
        {
            dc.DrawRectangle(b, null, new Rect(x - inf, y - inf, w + 2 * inf, h + 2 * inf));
        }

        static Pen P(Brush b, double th, PenLineCap cap)
        {
            return new Pen(b, th) { LineJoin = PenLineJoin.Miter, MiterLimit = 4, StartLineCap = cap, EndLineCap = cap };
        }

        // Una capa de la mira. "inf" ensancha cada trazo (contorno / brillo); la capa final usa inf = 0.
        static void Layer(DrawingContext dc, CrosshairLook l, double cx, double cy, double inf, Brush b, Brush bd)
        {
            double t = l.Thickness, ht = Math.Floor(t / 2), g = l.Gap, L = l.Size;
            switch (l.Style)
            {
                case 0: case 1: case 2: // cruz, cruz en T, aspa (la cruz girada 45°)
                    R(dc, b, cx + g, cy - ht, L, t, inf);
                    R(dc, b, cx - g - L, cy - ht, L, t, inf);
                    R(dc, b, cx - ht, cy + g, t, L, inf);
                    if (l.Style != 1) R(dc, b, cx - ht, cy - g - L, t, L, inf);
                    break;
                case 3: // círculo
                    dc.DrawEllipse(null, P(b, t + 2 * inf, PenLineCap.Flat), new Point(cx, cy), L, L);
                    break;
                case 5: // diamante
                {
                    var sg = new StreamGeometry();
                    using (var c = sg.Open())
                    {
                        c.BeginFigure(new Point(cx, cy - L), false, true);
                        c.LineTo(new Point(cx + L, cy), true, false);
                        c.LineTo(new Point(cx, cy + L), true, false);
                        c.LineTo(new Point(cx - L, cy), true, false);
                    }
                    sg.Freeze();
                    dc.DrawGeometry(null, P(b, t + 2 * inf, PenLineCap.Flat), sg);
                    break;
                }
                case 6: // cuadrado
                    dc.DrawRectangle(null, P(b, t + 2 * inf, PenLineCap.Flat), new Rect(cx - L, cy - L, 2 * L, 2 * L));
                    break;
                case 7: // chevron ^
                {
                    var sg = new StreamGeometry();
                    using (var c = sg.Open())
                    {
                        c.BeginFigure(new Point(cx - L, cy - g + L), false, false);
                        c.LineTo(new Point(cx, cy - g), true, false);
                        c.LineTo(new Point(cx + L, cy - g + L), true, false);
                    }
                    sg.Freeze();
                    dc.DrawGeometry(null, P(b, t + 2 * inf, PenLineCap.Round), sg);
                    break;
                }
                case 8: // esquinas
                {
                    double len = Math.Max(3, Math.Round(L * 0.6));
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                        {
                            double ox = cx + sx * L, oy = cy + sy * L;
                            R(dc, b, sx > 0 ? ox - len : ox, sy > 0 ? oy - t : oy, len, t, inf);
                            R(dc, b, sx > 0 ? ox - t : ox, sy > 0 ? oy - len : oy, t, len, inf);
                        }
                    break;
                }
            }
            if (l.Dot || l.Style == 4)
            {
                double n = Math.Max(1, l.DotSize);
                if (n <= 3) R(dc, bd, cx - Math.Floor(n / 2), cy - Math.Floor(n / 2), n, n, inf);
                else dc.DrawEllipse(bd, null, new Point(cx, cy), n / 2 + inf, n / 2 + inf);
            }
        }

        void DrawImage(DrawingContext dc, CrosshairLook l, double cx, double cy)
        {
            if (l.Image != imgKey)
            {
                imgKey = l.Image; img = null;
                try
                {
                    if (!string.IsNullOrEmpty(l.Image) && File.Exists(l.Image))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(l.Image, UriKind.Absolute); bi.EndInit(); bi.Freeze();
                        img = bi;
                    }
                }
                catch { img = null; }
            }
            if (img == null || img.PixelWidth <= 0 || img.PixelHeight <= 0) return;
            double s = l.ImageSize / (double)Math.Max(img.PixelWidth, img.PixelHeight);
            double w = img.PixelWidth * s, h = img.PixelHeight * s;
            dc.DrawImage(img, new Rect(cx - w / 2, cy - h / 2, w, h));
        }
    }

    // ------------------------------------------------------------------ ventana superpuesta
    static class ChNative
    {
        public const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    }

    // Ventana sin bordes, transparente, que no roba el foco ni los clics, y no sale en la barra de tareas ni en Alt+Tab.
    class CrosshairOverlay : Window
    {
        public readonly CrosshairVisual Visual = new CrosshairVisual();
        IntPtr hwnd;

        public CrosshairOverlay()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
            Focusable = false; IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; Width = 64; Height = 64;
            Content = Visual;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                int ex = ChNative.GetWindowLong(hwnd, ChNative.GWL_EXSTYLE);
                ChNative.SetWindowLong(hwnd, ChNative.GWL_EXSTYLE,
                    ex | ChNative.WS_EX_TRANSPARENT | ChNative.WS_EX_NOACTIVATE | ChNative.WS_EX_TOOLWINDOW | ChNative.WS_EX_LAYERED);
            };
        }

        // Centra la ventana en el monitor elegido y la vuelve a poner por encima de todo (los juegos a veces la tapan).
        public void Place(CrosshairSettings s)
        {
            if (hwnd == IntPtr.Zero) return;
            int px = CrosshairVisual.WindowPx(s.Look);
            var all = WF.Screen.AllScreens;
            var scr = (s.Monitor >= 0 && s.Monitor < all.Length) ? all[s.Monitor] : WF.Screen.PrimaryScreen;
            var b = scr.Bounds;
            int x = b.Left + b.Width / 2 - px / 2 + s.OffsetX;
            int y = b.Top + b.Height / 2 - px / 2 + s.OffsetY;
            ChNative.SetWindowPos(hwnd, ChNative.HWND_TOPMOST, x, y, px, px, ChNative.SWP_NOACTIVATE | ChNative.SWP_SHOWWINDOW);
        }
    }

    // ------------------------------------------------------------------ control (mostrar/ocultar, atajo, guardado)
    class CrosshairHost
    {
        const int HkId = 0x4742;
        public readonly CrosshairSettings S;
        public static readonly string[] Hotkeys = { "None", "F8", "F9", "F10", "Ctrl+Alt+C", "Ctrl+Alt+X", "Ctrl+Shift+C" };

        readonly bool live;
        CrosshairOverlay ov;
        HwndSource hk;
        DispatcherTimer topTimer, saveTimer;
        bool gameActive;

        public event Action Toggled;

        public CrosshairHost(CrosshairSettings s, bool live)
        {
            S = s; S.Fix();
            this.live = live;
            if (!live) return;
            var p = new HwndSourceParameters("GameBoostCrosshairHotkey") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            hk = new HwndSource(p);
            hk.AddHook(Hook);
            ApplyHotkey();
            Refresh();
        }

        public bool Showing { get { return ov != null; } }
        public bool ShouldShow { get { return S.Enabled && (!S.OnlyInGame || gameActive); } }

        public void SetGameActive(bool active) { gameActive = active; Refresh(); }

        public void Toggle()
        {
            S.Enabled = !S.Enabled;
            Refresh(); Save();
            if (Toggled != null) Toggled();
        }

        public void Refresh()
        {
            if (!live) return;
            if (ShouldShow)
            {
                if (ov == null)
                {
                    ov = new CrosshairOverlay();
                    ov.Show();
                    topTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    topTimer.Tick += (x, e) => { if (ov != null) ov.Place(S); };
                    topTimer.Start();
                }
                ov.Visual.Look = S.Look;
                ov.Place(S);
                ov.Visual.InvalidateVisual();
            }
            else if (ov != null)
            {
                topTimer.Stop();
                ov.Close(); ov = null;
            }
        }

        public void Save() { Store.Save("crosshair.json", S); }

        // Guarda poco después del último cambio (los deslizadores disparan muchos eventos seguidos).
        public void SaveSoon()
        {
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                saveTimer.Tick += (x, e) => { saveTimer.Stop(); Save(); };
            }
            saveTimer.Stop(); saveTimer.Start();
        }

        // Registra el atajo global. Devuelve false si otra aplicación ya lo usa.
        public bool ApplyHotkey()
        {
            if (hk == null) return true;
            ChNative.UnregisterHotKey(hk.Handle, HkId);
            uint mods, vk;
            if (!ParseHotkey(S.Hotkey, out mods, out vk)) return true;
            return ChNative.RegisterHotKey(hk.Handle, HkId, mods | 0x4000 /* sin repetición */, vk);
        }

        static bool ParseHotkey(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(s) || s == "None") return false;
            foreach (var part in s.Split('+'))
            {
                if (part == "Ctrl") mods |= 2;
                else if (part == "Alt") mods |= 1;
                else if (part == "Shift") mods |= 4;
                else if (part.Length == 1) vk = (uint)char.ToUpperInvariant(part[0]);
                else if (part.Length >= 2 && part[0] == 'F')
                {
                    int n;
                    if (int.TryParse(part.Substring(1), out n) && n >= 1 && n <= 24) vk = (uint)(0x70 + n - 1);
                }
            }
            return vk != 0;
        }

        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 && w.ToInt32() == HkId) { handled = true; Toggle(); }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (saveTimer != null) { saveTimer.Stop(); }
            Save();
            if (topTimer != null) topTimer.Stop();
            if (ov != null) { ov.Close(); ov = null; }
            if (hk != null) { ChNative.UnregisterHotKey(hk.Handle, HkId); hk.Dispose(); hk = null; }
        }
    }
}
