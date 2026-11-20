// Entrenador de puntería de GameBoost (BLAD Studios)
//
// Minijuego dentro de la app para calentar antes de jugar: Flicks, Gridshot, Tracking y Reacción.
// Usa el crosshair del usuario como cursor y guarda las marcas en aim.json.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public class AimHistory { public List<AimResult> Items = new List<AimResult>(); }

    class AimTrainer : Grid
    {
        public static readonly string[] Modes = { "flick", "grid", "track", "react" };
        public static readonly double[] Radii = { 14, 22, 32 };
        const int FlickCount = 30, ReactRounds = 5;
        const double Duration = 30;

        public static string ModeName(string m)
        {
            return m == "flick" ? "Flicks" : m == "grid" ? "Gridshot" : m == "track" ? "Tracking" : "Reacción";
        }

        public static string ModeDesc(string m)
        {
            if (m == "flick") return "Flicks: 30 objetivos, uno a uno. Cuentan la velocidad y la precisión.";
            if (m == "grid") return "Gridshot: 3 objetivos a la vez durante 30 segundos. Cada fallo resta puntos.";
            if (m == "track") return "Tracking: sigue el objetivo con el cursor durante 30 segundos, sin hacer clic.";
            return "Reacción: haz clic en cuanto la zona se ponga verde. 5 rondas.";
        }

        // En Reacción la marca es el tiempo medio (menos es mejor); en el resto, la puntuación (más es mejor).
        public static bool LowerIsBetter(string m) { return m == "react"; }

        class Target { public Ellipse E; public Point C; public double R; public Vector V; public double TurnAt; }

        static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        static readonly Brush BgIdle = B("#0B0E14"), BgWait = B("#3A1418"), BgGo = B("#123A22"),
                              TargetFill = B("#7B61FF"), TargetOn = B("#22C55E"), HudFg = B("#8A90A2"), MsgFg = B("#E8EAF0");

        readonly Canvas field;
        readonly CrosshairVisual cursor;
        readonly TextBlock hud, msg;
        readonly List<Target> targets = new List<Target>();
        readonly List<double> reacts = new List<double>();
        readonly Random rnd = new Random();
        readonly Stopwatch clock = new Stopwatch();
        readonly DispatcherTimer tick;

        public string Mode = "flick";
        public int SizeIdx = 1;
        public bool EscHint;
        bool running, green;
        int hits, misses, round;
        double lastHitAt, sumMs, onTarget, lastTick, greenAt, nextGreenAt;

        public event Action<AimResult> Finished;

        public AimTrainer(CrosshairLook look)
        {
            ClipToBounds = true;
            field = new Canvas { Background = BgIdle, Cursor = Cursors.None, ClipToBounds = true };
            Children.Add(field);
            cursor = new CrosshairVisual { IsHitTestVisible = false, Visibility = Visibility.Hidden };
            SetLook(look);
            field.Children.Add(cursor);
            hud = new TextBlock { Foreground = HudFg, FontSize = 13, Margin = new Thickness(16, 12, 16, 0), IsHitTestVisible = false,
                                  HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            msg = new TextBlock { Foreground = MsgFg, FontSize = 18, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                                  TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                  IsHitTestVisible = false, Margin = new Thickness(30) };
            Children.Add(hud);
            Children.Add(msg);

            field.MouseMove += (s, e) => MoveCursor(e.GetPosition(field));
            field.MouseEnter += (s, e) => cursor.Visibility = Visibility.Visible;
            field.MouseLeave += (s, e) => cursor.Visibility = Visibility.Hidden;
            field.MouseLeftButtonDown += (s, e) => { e.Handled = true; Click(e.GetPosition(field)); };
            tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            tick.Tick += (s, e) => Tick();
            Unloaded += (s, e) => Stop();
            Idle();
        }

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        int Acc() { int n = hits + misses; return n == 0 ? 100 : (int)Math.Round(100.0 * hits / n); }

        // Usa el crosshair del usuario como cursor (siempre opaco para que se vea bien).
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
            msg.Text = L.T("Haz clic para empezar") + (EscHint ? "\n" + L.T("Esc para salir") : "");
        }

        public void Stop()
        {
            if (!running) return;
            running = false; green = false;
            tick.Stop(); clock.Stop();
            ClearTargets();
            field.Background = BgIdle;
            Idle();
        }

        void MoveCursor(Point p)
        {
            Canvas.SetLeft(cursor, p.X - cursor.Width / 2);
            Canvas.SetTop(cursor, p.Y - cursor.Height / 2);
        }

        void Start()
        {
            ClearTargets();
            hits = misses = round = 0; sumMs = onTarget = lastTick = lastHitAt = 0;
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
                c = new Point(r + 10 + rnd.NextDouble() * Math.Max(1, w - 2 * r - 20),
                              r + 40 + rnd.NextDouble() * Math.Max(1, h - 2 * r - 50));
            }
            while (++tries < 25 && targets.Any(t => (t.C - c).Length < t.R + r + 12));
            var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = TargetFill, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
            var tg = new Target { E = e, C = c, R = r };
            if (moving) { NewDirection(tg); }
            Place(tg);
            targets.Add(tg);
            field.Children.Insert(0, e); // el cursor queda siempre por encima
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
            if (Mode == "track") return;
            if (Mode == "react")
            {
                if (green)
                {
                    reacts.Add((Now - greenAt) * 1000);
                    round++; green = false;
                    if (round >= ReactRounds) { Finish(); return; }
                    NextReactRound(false);
                }
                else { misses++; NextReactRound(true); }
                UpdateHud();
                return;
            }
            var hit = targets.FirstOrDefault(t => (t.C - p).Length <= t.R);
            if (hit == null) { misses++; UpdateHud(); return; }
            hits++;
            double now = Now;
            sumMs += (now - lastHitAt) * 1000;
            lastHitAt = now;
            field.Children.Remove(hit.E);
            targets.Remove(hit);
            if (Mode == "flick" && hits >= FlickCount) { Finish(); return; }
            Spawn(false);
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
            if (Mode == "react")
            {
                if (!green && now >= nextGreenAt) { green = true; greenAt = now; field.Background = BgGo; msg.Text = L.T("¡Clic!"); }
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
                bool on = field.IsMouseOver && (Mouse.GetPosition(field) - t.C).Length <= t.R;
                if (on) onTarget += dt;
                t.E.Fill = on ? TargetOn : TargetFill;
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
        }

        void Finish()
        {
            running = false; green = false;
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
            msg.Text = text + "\n" + L.T("Haz clic para jugar otra vez") + (EscHint ? "\n" + L.T("Esc para salir") : "");
            if (Finished != null) Finished(r);
        }
    }
}
