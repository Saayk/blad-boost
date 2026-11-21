// Entrenador de puntería de GameBoost (BLAD Studios)
//
// Minijuego para calentar antes de jugar: Flicks, Gridshot, Tracking y Reacción, con sensación de disparo
// (arma con retroceso, fogonazo, sonido, impactos, agujeros de bala, munición y recarga).
// Usa el crosshair del usuario como mira y guarda las marcas en aim.json.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GameBoost
{
    public class AimResult
    {
        public string Mode;
        public long Date;
        public int Score, Hits, Misses;
        public double Accuracy, AvgMs;
    }

    public class AimHistory
    {
        public List<AimResult> Items = new List<AimResult>();
        public bool Mute; // sonido de disparo desactivado
    }

    class AimTrainer : Grid
    {
        public static readonly string[] Modes = { "flick", "grid", "track", "react" };
        public static readonly double[] Radii = { 14, 22, 32 };
        public static bool SoundOn = true;
        const int FlickCount = 30, ReactRounds = 5, Mag = 12;
        const double Duration = 30, ReloadTime = 0.9, AutoFireGap = 0.1;

        public static string ModeName(string m)
        {
            return m == "flick" ? "Flicks" : m == "grid" ? "Gridshot" : m == "track" ? "Tracking" : "Reacción";
        }

        public static string ModeDesc(string m)
        {
            if (m == "flick") return "Flicks: 30 objetivos, uno a uno. Cuentan la velocidad y la precisión. Cargador de 12 balas; R para recargar.";
            if (m == "grid") return "Gridshot: 3 objetivos a la vez durante 30 segundos. Cada fallo resta puntos. Cargador de 12 balas; R para recargar.";
            if (m == "track") return "Tracking: mantén pulsado para disparar en automático y sigue el objetivo durante 30 segundos.";
            return "Reacción: dispara en cuanto la zona se ponga verde. 5 rondas.";
        }

        // En Reacción la marca es el tiempo medio (menos es mejor); en el resto, la puntuación (más es mejor).
        public static bool LowerIsBetter(string m) { return m == "react"; }

        class Target { public Ellipse E; public Point C; public double R; public Vector V; public double TurnAt; }

        static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        static readonly Brush BgIdle = B("#0B0B0D"), BgWait = B("#3A1418"), BgGo = B("#123A22"),
                              TargetOn = B("#22C55E"), HudFg = B("#A1A1AA"), MsgFg = B("#F5F5F7"), Accent = B("#E5242F");
        static readonly Brush TargetFill = MakeTargetBrush();

        static Brush MakeTargetBrush()
        {
            var g = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3) };
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF5A64"), 0));
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#C8101E"), 1));
            g.Freeze();
            return g;
        }

        readonly Canvas field;
        readonly TranslateTransform shake = new TranslateTransform();
        readonly CrosshairVisual cursor;
        readonly TranslateTransform cursorKick = new TranslateTransform();
        readonly TextBlock hud, msg, ammoText;
        readonly Canvas gun;
        // Ángulo positivo = la boca del cañón (a la izquierda) sube y apunta hacia el centro de la pantalla.
        readonly RotateTransform gunRot = new RotateTransform(16, 120, 70);
        readonly TranslateTransform gunMove = new TranslateTransform();
        readonly Ellipse flash;
        readonly List<Target> targets = new List<Target>();
        readonly List<double> reacts = new List<double>();
        readonly Random rnd = new Random();
        readonly Stopwatch clock = new Stopwatch();
        readonly DispatcherTimer tick;

        public string Mode = "flick";
        public int SizeIdx = 1;
        public bool EscHint;
        bool running, green, firing, reloading;
        int hits, misses, round, ammo = Mag;
        double lastHitAt, sumMs, onTarget, lastTick, greenAt, nextGreenAt, reloadEnd, nextShotAt, gunBase = 16;

        public event Action<AimResult> Finished;

        public AimTrainer(CrosshairLook look)
        {
            ClipToBounds = true;
            field = new Canvas { Background = BgIdle, Cursor = Cursors.None, ClipToBounds = true, Focusable = true, FocusVisualStyle = null, RenderTransform = shake };
            Children.Add(field);
            cursor = new CrosshairVisual { IsHitTestVisible = false, Visibility = Visibility.Hidden, RenderTransform = cursorKick };
            SetLook(look);
            field.Children.Add(cursor);

            gun = BuildGun(out flash);
            gun.HorizontalAlignment = HorizontalAlignment.Right;
            gun.VerticalAlignment = VerticalAlignment.Bottom;
            gun.Margin = new Thickness(0, 0, 30, -6);
            var tg = new TransformGroup();
            tg.Children.Add(gunRot); tg.Children.Add(gunMove);
            gun.RenderTransform = tg;
            Children.Add(gun);

            hud = new TextBlock { Foreground = HudFg, FontSize = 13, Margin = new Thickness(16, 12, 16, 0), IsHitTestVisible = false,
                                  HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            msg = new TextBlock { Foreground = MsgFg, FontSize = 18, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                                  TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                  IsHitTestVisible = false, Margin = new Thickness(30) };
            ammoText = new TextBlock { Foreground = MsgFg, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(18, 0, 0, 14), IsHitTestVisible = false,
                                       HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            Children.Add(hud); Children.Add(msg); Children.Add(ammoText);

            field.MouseMove += (s, e) => MoveCursor(e.GetPosition(field));
            field.MouseEnter += (s, e) => cursor.Visibility = Visibility.Visible;
            field.MouseLeave += (s, e) => { cursor.Visibility = Visibility.Hidden; firing = false; };
            field.MouseLeftButtonDown += (s, e) => { e.Handled = true; field.Focus(); Click(e.GetPosition(field)); };
            field.MouseLeftButtonUp += (s, e) => firing = false;
            field.KeyDown += (s, e) => { if (e.Key == Key.R) { e.Handled = true; Reload(); } };
            tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            tick.Tick += (s, e) => Tick();
            Unloaded += (s, e) => Stop();
            Idle();
        }

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        int Acc() { int n = hits + misses; return n == 0 ? 100 : (int)Math.Round(100.0 * hits / n); }
        bool UsesAmmo { get { return Mode == "flick" || Mode == "grid"; } }

        // ------------------------------------------------------------ arma
        // Pistola en primera persona: corredera, empuñadura, franja roja y fogonazo en la boca del cañón.
        static Canvas BuildGun(out Ellipse flash)
        {
            var c = new Canvas { Width = 170, Height = 120, IsHitTestVisible = false };
            var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            metal.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3A3A42"), 0));
            metal.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#15151A"), 1));
            var body = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M10,10 L160,10 Q166,10 166,16 L166,40 L130,40 L140,112 Q141,118 134,118 L104,118 Q98,118 97,112 L90,64 L76,64 Q66,64 63,56 L60,40 L10,40 Z"),
                Fill = metal, Stroke = B("#4A4A52"), StrokeThickness = 1.2
            };
            c.Children.Add(body);
            var stripe = new Rectangle { Width = 118, Height = 4, Fill = Accent, RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(stripe, 30); Canvas.SetTop(stripe, 20);
            c.Children.Add(stripe);
            var sight = new Rectangle { Width = 8, Height = 6, Fill = B("#2A2A30") };
            Canvas.SetLeft(sight, 16); Canvas.SetTop(sight, 5);
            c.Children.Add(sight);
            var fg = new RadialGradientBrush();
            fg.GradientStops.Add(new GradientStop(Colors.White, 0));
            fg.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFD27A"), 0.35));
            fg.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FF7A1A"), 1));
            flash = new Ellipse { Width = 64, Height = 40, Fill = fg, Opacity = 0 };
            Canvas.SetLeft(flash, -50); Canvas.SetTop(flash, 5);
            c.Children.Add(flash);
            return c;
        }

        // Disparo: sonido, fogonazo, retroceso del arma y de la mira, y un leve temblor.
        void FireEffects()
        {
            GunSound.Play();
            flash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            gunRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(gunBase + 9, gunBase, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            gunMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            cursorKick.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-7, 0, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
            shake.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(rnd.Next(-3, 4), 0, TimeSpan.FromMilliseconds(70)));
            shake.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rnd.Next(-2, 3), 0, TimeSpan.FromMilliseconds(70)));
        }

        void Reload()
        {
            if (!running || !UsesAmmo || reloading || ammo == Mag) return;
            reloading = true;
            reloadEnd = Now + ReloadTime;
            var dip = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            dip.KeyFrames.Add(new EasingDoubleKeyFrame(70, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(ReloadTime * 0.4)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            dip.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(ReloadTime)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
            gunMove.BeginAnimation(TranslateTransform.YProperty, dip);
            UpdateHud();
        }

        // ------------------------------------------------------------ efectos de impacto
        void HitMarker(Point p)
        {
            var c = new Canvas { IsHitTestVisible = false };
            foreach (var d in new[] { new Vector(-1, -1), new Vector(1, -1), new Vector(-1, 1), new Vector(1, 1) })
                c.Children.Add(new Line { X1 = d.X * 5, Y1 = d.Y * 5, X2 = d.X * 11, Y2 = d.Y * 11, Stroke = Brushes.White, StrokeThickness = 2 });
            Canvas.SetLeft(c, p.X); Canvas.SetTop(c, p.Y);
            AddFx(c, 220);
        }

        void Burst(Point p, double r)
        {
            for (int i = 0; i < 9; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2, dist = r + 18 + rnd.NextDouble() * 34, size = 3 + rnd.NextDouble() * 4;
                var e = new Ellipse { Width = size, Height = size, Fill = i % 3 == 0 ? Brushes.White : Accent, IsHitTestVisible = false };
                var t = new TranslateTransform();
                e.RenderTransform = t;
                Canvas.SetLeft(e, p.X - size / 2); Canvas.SetTop(e, p.Y - size / 2);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(a) * dist, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(a) * dist, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
                AddFx(e, 380);
            }
        }

        void BulletHole(Point p)
        {
            var hole = new Ellipse { Width = 7, Height = 7, Fill = B("#050505"), Stroke = B("#55555E"), StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetLeft(hole, p.X - 3.5); Canvas.SetTop(hole, p.Y - 3.5);
            field.Children.Insert(Math.Max(0, field.Children.Count - 1), hole);
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
            fade.Completed += (s, e) => field.Children.Remove(hole);
            hole.BeginAnimation(OpacityProperty, fade);
        }

        // Efecto temporal que se desvanece y se elimina solo (queda por debajo de la mira).
        void AddFx(UIElement el, int ms)
        {
            field.Children.Insert(Math.Max(0, field.Children.Count - 1), el);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms));
            fade.Completed += (s, e) => field.Children.Remove(el);
            el.BeginAnimation(OpacityProperty, fade);
        }

        void PopTarget(Target t)
        {
            targets.Remove(t);
            var e = t.E;
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(1, 1);
            e.RenderTransform = sc;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.5, TimeSpan.FromMilliseconds(140)));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.5, TimeSpan.FromMilliseconds(140)));
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (s, ev) => field.Children.Remove(e);
            e.BeginAnimation(OpacityProperty, fade);
            Burst(t.C, t.R);
        }

        // ------------------------------------------------------------ estado
        // Usa el crosshair del usuario como mira (siempre opaco para que se vea bien).
        public void SetLook(CrosshairLook look)
        {
            var l = look.Clone();
            l.Opacity = 100;
            cursor.Look = l;
            cursor.Width = cursor.Height = CrosshairVisual.WindowPx(l);
            cursor.InvalidateVisual();
        }

        public void SetMode(string m) { Stop(); Mode = m; Idle(); }
        public void SetSize(int s) { Stop(); SizeIdx = Math.Max(0, Math.Min(Radii.Length - 1, s)); Idle(); }

        void Idle()
        {
            hud.Text = L.T(ModeName(Mode));
            msg.Text = L.T("Dispara para empezar") + (EscHint ? "\n" + L.T("Esc para salir") : "");
            ammoText.Text = UsesAmmo ? Mag + " / " + Mag : "";
        }

        public void Stop()
        {
            if (!running) return;
            running = false; green = false; firing = false; reloading = false;
            tick.Stop(); clock.Stop();
            ClearTargets();
            field.Background = BgIdle;
            Idle();
        }

        void MoveCursor(Point p)
        {
            Canvas.SetLeft(cursor, p.X - cursor.Width / 2);
            Canvas.SetTop(cursor, p.Y - cursor.Height / 2);
            // El arma apunta ligeramente hacia donde está la mira.
            double w = Math.Max(1, field.ActualWidth), h = Math.Max(1, field.ActualHeight);
            gunBase = 16 + (0.5 - p.X / w) * 12 + (0.5 - p.Y / h) * 10;
            gunRot.Angle = gunBase;
        }

        void Start()
        {
            ClearTargets();
            hits = misses = round = 0; sumMs = onTarget = lastTick = lastHitAt = 0;
            ammo = Mag; reloading = false; firing = false;
            reacts.Clear();
            running = true; green = false;
            msg.Text = "";
            field.Background = BgIdle;
            clock.Restart();
            if (Mode == "flick") Spawn(false);
            else if (Mode == "grid") { Spawn(false); Spawn(false); Spawn(false); }
            else if (Mode == "track") Spawn(true);
            else NextReactRound(false);
            tick.Start();
            UpdateHud();
        }

        void Spawn(bool moving)
        {
            double r = Radii[SizeIdx] * (moving ? 1.25 : 1);
            double w = Math.Max(field.ActualWidth, 4 * r + 20), h = Math.Max(field.ActualHeight, 4 * r + 60);
            Point c; int tries = 0;
            do
            {
                c = new Point(r + 10 + rnd.NextDouble() * Math.Max(1, w - 2 * r - 220),
                              r + 40 + rnd.NextDouble() * Math.Max(1, h - 2 * r - 50));
            }
            while (++tries < 25 && targets.Any(t => (t.C - c).Length < t.R + r + 12));
            var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = TargetFill, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
            var tg = new Target { E = e, C = c, R = r };
            if (moving) NewDirection(tg);
            Place(tg);
            targets.Add(tg);
            field.Children.Insert(0, e);
            // Aparece creciendo
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(0.3, 0.3);
            e.RenderTransform = sc;
            var grow = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        void NewDirection(Target t)
        {
            double a = rnd.NextDouble() * Math.PI * 2, speed = 180 + rnd.NextDouble() * 140;
            t.V = new Vector(Math.Cos(a) * speed, Math.Sin(a) * speed);
            t.TurnAt = Now + 0.5 + rnd.NextDouble() * 0.9;
        }

        static void Place(Target t) { Canvas.SetLeft(t.E, t.C.X - t.R); Canvas.SetTop(t.E, t.C.Y - t.R); }

        void ClearTargets()
        {
            foreach (var t in targets) field.Children.Remove(t.E);
            targets.Clear();
        }

        void Click(Point p)
        {
            if (!running) { Start(); return; }
            if (Mode == "track") { firing = true; nextShotAt = Now; return; }
            if (Mode == "react")
            {
                FireEffects();
                if (green)
                {
                    reacts.Add((Now - greenAt) * 1000);
                    HitMarker(p);
                    round++; green = false;
                    if (round >= ReactRounds) { Finish(); return; }
                    NextReactRound(false);
                }
                else { misses++; NextReactRound(true); }
                UpdateHud();
                return;
            }
            if (reloading) return;
            if (ammo <= 0) { Reload(); return; }
            ammo--;
            FireEffects();
            var hit = targets.FirstOrDefault(t => (t.C - p).Length <= t.R);
            if (hit == null) { misses++; BulletHole(p); }
            else
            {
                hits++;
                double now = Now;
                sumMs += (now - lastHitAt) * 1000;
                lastHitAt = now;
                HitMarker(p);
                PopTarget(hit);
                if (Mode == "flick" && hits >= FlickCount) { Finish(); return; }
                Spawn(false);
            }
            if (ammo == 0) Reload();
            UpdateHud();
        }

        void NextReactRound(bool early)
        {
            field.Background = BgWait;
            green = false;
            nextGreenAt = Now + 1.2 + rnd.NextDouble() * 2.3;
            string wait = L.F("Ronda {0}/{1} · Espera al verde...", round + 1, ReactRounds);
            msg.Text = early ? L.T("¡Demasiado pronto! Espera al verde.") + "\n" + wait : wait;
        }

        void Tick()
        {
            if (!running) return;
            double now = Now, dt = now - lastTick;
            lastTick = now;
            if (reloading && now >= reloadEnd) { reloading = false; ammo = Mag; UpdateHud(); }
            if (Mode == "react")
            {
                if (!green && now >= nextGreenAt) { green = true; greenAt = now; field.Background = BgGo; msg.Text = L.T("¡Dispara!"); }
                return;
            }
            if (Mode == "track" && targets.Count > 0)
            {
                var t = targets[0];
                if (now >= t.TurnAt) NewDirection(t);
                t.C += t.V * dt;
                double w = field.ActualWidth, h = field.ActualHeight;
                if (t.C.X < t.R) { t.C.X = t.R; t.V.X = Math.Abs(t.V.X); }
                if (t.C.X > w - t.R) { t.C.X = w - t.R; t.V.X = -Math.Abs(t.V.X); }
                if (t.C.Y < t.R + 30) { t.C.Y = t.R + 30; t.V.Y = Math.Abs(t.V.Y); }
                if (t.C.Y > h - t.R) { t.C.Y = h - t.R; t.V.Y = -Math.Abs(t.V.Y); }
                Place(t);
                var mp = Mouse.GetPosition(field);
                bool on = field.IsMouseOver && (mp - t.C).Length <= t.R;
                if (firing && on) onTarget += dt;
                t.E.Fill = firing && on ? TargetOn : TargetFill;
                if (firing && now >= nextShotAt)
                {
                    nextShotAt = now + AutoFireGap;
                    FireEffects();
                    if (on) { hits++; HitMarker(mp); } else { misses++; BulletHole(mp); }
                }
            }
            if ((Mode == "grid" || Mode == "track") && now >= Duration) { Finish(); return; }
            UpdateHud();
        }

        void UpdateHud()
        {
            int left = Math.Max(0, (int)Math.Ceiling(Duration - Now));
            if (Mode == "flick") hud.Text = L.F("Objetivos {0}/{1} · Precisión {2}%", hits, FlickCount, Acc());
            else if (Mode == "grid") hud.Text = L.F("Tiempo {0} s · Aciertos {1} · Precisión {2}%", left, hits, Acc());
            else if (Mode == "track") hud.Text = L.F("Tiempo {0} s · En el objetivo {1}%", left, (int)Math.Round(100 * onTarget / Math.Max(0.001, Now)));
            else hud.Text = L.F("Ronda {0}/{1}", Math.Min(round + 1, ReactRounds), ReactRounds);
            ammoText.Text = !UsesAmmo ? "" : reloading ? L.T("Recargando...") : ammo + " / " + Mag;
            ammoText.Foreground = UsesAmmo && !reloading && ammo <= 3 ? Accent : MsgFg;
        }

        void Finish()
        {
            running = false; green = false; firing = false; reloading = false;
            tick.Stop(); clock.Stop();
            ClearTargets();
            field.Background = BgIdle;
            var r = new AimResult { Mode = Mode, Date = DateTime.Now.Ticks / TimeSpan.TicksPerSecond, Hits = hits, Misses = misses };
            string text;
            if (Mode == "flick")
            {
                r.Accuracy = Acc();
                r.AvgMs = hits > 0 ? sumMs / hits : 0;
                r.Score = (int)Math.Round(r.Accuracy / 100 * 600000 / Math.Max(150, r.AvgMs));
                text = L.F("Puntuación {0} · Precisión {1}% · {2} ms por objetivo", r.Score, (int)r.Accuracy, (int)Math.Round(r.AvgMs));
            }
            else if (Mode == "grid")
            {
                r.Accuracy = Acc();
                r.Score = Math.Max(0, hits * 100 - misses * 50);
                text = L.F("Puntuación {0} · {1} aciertos · Precisión {2}%", r.Score, hits, (int)r.Accuracy);
            }
            else if (Mode == "track")
            {
                r.Accuracy = Math.Round(Math.Min(100, 100 * onTarget / Duration), 1);
                r.Score = (int)Math.Round(r.Accuracy * 10);
                text = L.F("Puntuación {0} · En el objetivo {1}%", r.Score, (int)Math.Round(r.Accuracy));
            }
            else
            {
                r.Hits = reacts.Count;
                r.AvgMs = reacts.Count > 0 ? reacts.Average() : 0;
                r.Score = (int)Math.Round(r.AvgMs);
                text = L.F("Tiempo de reacción medio: {0} ms", r.Score);
            }
            hud.Text = L.T(ModeName(Mode));
            ammoText.Text = UsesAmmo ? Mag + " / " + Mag : "";
            msg.Text = text + "\n" + L.T("Dispara para jugar otra vez") + (EscHint ? "\n" + L.T("Esc para salir") : "");
            if (Finished != null) Finished(r);
        }
    }

    // Sonido de disparo sintetizado (ruido con caída rápida + golpe grave). Se genera una vez en memoria.
    static class GunSound
    {
        static SoundPlayer player;

        public static void Play()
        {
            if (!AimTrainer.SoundOn) return;
            try
            {
                if (player == null) { player = new SoundPlayer(new MemoryStream(Build())); player.Load(); }
                player.Play();
            }
            catch { }
        }

        static byte[] Build()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.22);
            var rnd = new Random(7);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            double low = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double noise = rnd.NextDouble() * 2 - 1;
                low += (noise - low) * 0.18;                                   // ruido filtrado: cuerpo del disparo
                double crack = noise * Math.Exp(-t * 90);                       // chasquido inicial
                double body = low * Math.Exp(-t * 22) * 1.6;
                double thump = Math.Sin(2 * Math.PI * 70 * t) * Math.Exp(-t * 28); // golpe grave
                double v = (crack * 0.5 + body + thump * 0.6) * 0.42;
                w.Write((short)(Math.Max(-1, Math.Min(1, v)) * short.MaxValue));
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
