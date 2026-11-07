using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("GameBoost")]
[assembly: AssemblyDescription("GameBoost - Optimizador de PC para juegos")]
[assembly: AssemblyCompany("BLAD Studios")]
[assembly: AssemblyProduct("GameBoost")]
[assembly: AssemblyCopyright("© 2026 BLAD Studios")]
[assembly: AssemblyTrademark("GameBoost es una marca de BLAD Studios")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]
[assembly: AssemblyInformationalVersion("2.2.0")]

namespace GameBoost
{
    static class Program
    {
        public static bool SelfTest;

        [STAThread]
        static int Main(string[] args)
        {
            var a = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
            SelfTest = a.Contains("--selftest");
            var settings = Store.Load<Settings>("settings.json");
            L.Init(a.Contains("--lang-en") ? "en" : a.Contains("--lang-es") ? "es" : settings.Lang);
            if (a.Contains("--uninstall")) return RunUninstall();
            if (a.Contains("--autostart-on")) { Installer.SetAutostart(true); return Installer.AutostartEnabled() ? 0 : 1; }
            if (a.Contains("--autostart-off")) { Installer.SetAutostart(false); return Installer.AutostartEnabled() ? 1 : 0; }
            if (a.Contains("--benchtest"))
            {
                var r = Bench.Run((p, s) => { }, false);
                Directory.CreateDirectory(Store.Dir);
                File.WriteAllText(Path.Combine(Store.Dir, "benchtest.txt"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(r));
                return 0;
            }

            Mutex mutex = null;
            EventWaitHandle showEvt = null;
            if (!SelfTest)
            {
                bool created;
                mutex = new Mutex(true, "BLADStudios.GameBoost.Instance", out created);
                showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "BLADStudios.GameBoost.Show");
                if (!created) { showEvt.Set(); return 0; }
            }

            int code = 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { Directory.CreateDirectory(Store.Dir); File.WriteAllText(Path.Combine(Store.Dir, "error.log"), e.Exception.ToString()); } catch { }
                e.Handled = true;
                if (SelfTest) { code = 1; app.Shutdown(1); return; }
                MessageBox.Show(e.Exception.Message, "GameBoost");
            };
            Window w;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) w = (Window)XamlReader.Load(st);
            try { w.Icon = BitmapFrame.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")); } catch { }
            L.Translate(w);
            var c = new Controller(w, settings, showEvt);
            if (SelfTest)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -20000; w.Top = -20000; w.ShowActivated = false; w.ShowInTaskbar = false;
                w.Show();
                c.RunSelfTest();
            }
            else if (!a.Contains("--tray")) w.Show();
            app.Run();
            GC.KeepAlive(mutex);
            return code;
        }

        static int RunUninstall()
        {
            var r = MessageBox.Show(L.T("¿Quieres desinstalar GameBoost?") + "\n\n" +
                L.T("Sí: desinstalar y restaurar tu configuración original de Windows.") + "\n" +
                L.T("No: desinstalar y mantener la configuración actual."),
                "GameBoost", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return 0;
            var games = Store.Load<GameList>("games.json");
            if (games.Games != null) foreach (var g in games.Games) Sys.Ifeo(g.Proc, "Normal");
            Installer.Uninstall(r == MessageBoxResult.Yes);
            MessageBox.Show(L.T("GameBoost se desinstaló correctamente."), "GameBoost", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }
    }

    class Rec
    {
        public int Sev; // 0 ok, 1 info, 2 medio, 3 alto
        public string Title, Desc, Btn;
        public Action Do;
    }

    class AppRec
    {
        public string Name, Desc, Url, Key, Glyph, Price;
        public bool Mine;
    }

    class BenchMetric
    {
        public string Glyph, Title, Fmt, Unit;
        public bool Higher;
        public Func<BenchResult, double> Get;
    }

    class Controller
    {
        const string UrlNvidia = "https://www.nvidia.com/es-la/software/nvidia-app/";
        const string UrlAmdGpu = "https://www.amd.com/es/products/software/adrenalin.html";
        const string UrlAmdDrivers = "https://www.amd.com/es/support/download/drivers.html";
        const string UrlIntel = "https://www.intel.com/content/www/us/en/support/intel-driver-support-assistant.html";

        static readonly Dictionary<string, string> KnownGames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "FortniteClient-Win64-Shipping", "Fortnite" }, { "VALORANT-Win64-Shipping", "VALORANT" }, { "cs2", "Counter-Strike 2" },
            { "r5apex", "Apex Legends" }, { "r5apex_dx12", "Apex Legends" }, { "RocketLeague", "Rocket League" }, { "GTA5", "GTA V" },
            { "GTA5_Enhanced", "GTA V" }, { "Overwatch", "Overwatch 2" }, { "League of Legends", "League of Legends" },
            { "RobloxPlayerBeta", "Roblox" }, { "Minecraft.Windows", "Minecraft" }, { "cod", "Call of Duty" }, { "FiveM", "FiveM" },
            { "eldenring", "Elden Ring" }, { "Marvel-Win64-Shipping", "Marvel Rivals" }, { "RainbowSix", "Rainbow Six Siege" },
            { "PUBG-Win64-Shipping", "PUBG" }, { "dota2", "Dota 2" }, { "TslGame", "PUBG" }, { "FC25", "EA SPORTS FC 25" }, { "FC26", "EA SPORTS FC 26" }
        };

        readonly Window win;
        Settings settings;
        GameList games;
        BenchList bench;
        SysInfo sys;
        List<string> installedApps;
        readonly List<string> adaptations = new List<string>();
        long junkBytes = -1;
        bool rebootPending, closing, statesLoaded, realExit, benchRunning, restoreArmed;
        readonly Dictionary<string, CheckBox> tweakSwitches = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, CheckBox> cleanSwitches = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, TextBlock> cleanSizes = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, TextBlock> catCounters = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>();

        TextBlock cpuVal, cpuSub, ramVal, ramSub, pingVal, pingSub, diskVal, diskSub;
        ScaleTransform cpuBar, ramBar, pingBar, diskBar;
        long lastIdle, lastKernel, lastUser;
        DispatcherTimer statTimer, toastTimer;

        WF.NotifyIcon tray;
        WF.ToolStripItem trayModeItem;
        UpdateInfo pendingUpdate;

        volatile bool sessionActive, sessionBusy;
        volatile string ignoredProc, sessionProc;
        DateTime sessionStart;
        Game sessionGame;
        string sessionName;
        List<string> stoppedSvcs = new List<string>(), closedApps = new List<string>();
        bool timerHeld;

        Game editing;
        bool deleteArmed;

        public Controller(Window w, Settings s, EventWaitHandle showEvt)
        {
            win = w;
            settings = s;
            games = Store.Load<GameList>("games.json");
            if (games.Games == null) games.Games = new List<Game>();
            bench = Store.Load<BenchList>("bench.json");
            if (bench.Items == null) bench.Items = new List<BenchResult>();

            E<TextBlock>("AboutTitle").Text = "GameBoost " + Installer.Version;
            E<TextBlock>("AboutRight").Text = "© 2026 BLAD Studios\n" + (Installer.RunningInstalled ? Tr("Versión instalada") : Tr("Versión portátil"));
            WireChrome();
            WireNav();
            BuildStats();
            BuildTweaks();
            BuildClean();
            BuildGeneral();
            BuildSettings();
            WireGames();
            WireDashboard();
            WireBench();
            BuildBench();
            BuildTips();
            BuildApps();
            RebuildGames();
            RebuildQuick();
            SetGreeting();
            SetRing(0, false);
            UpdateBackupInfo();

            statTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            statTimer.Tick += (x, e) => UpdateStats();
            statTimer.Start();
            new Thread(PingLoop) { IsBackground = true }.Start();

            if (!Program.SelfTest)
            {
                InitTray();
                new Thread(WatchLoop) { IsBackground = true }.Start();
                if (showEvt != null) new Thread(() => { while (true) { showEvt.WaitOne(); Ui(ShowWindow); } }) { IsBackground = true }.Start();
                if (settings.CheckUpdates)
                {
                    var ut = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                    ut.Tick += (x, e) => { ut.Stop(); CheckUpdates(false); };
                    ut.Start();
                }
            }
            win.Dispatcher.BeginInvoke(new Action(StartupScan), DispatcherPriority.Background);
            win.Closing += OnWindowClosing;
            Log("GameBoost " + Installer.Version + " " + Tr("iniciado"));
        }

        // ============================================================ helpers
        T E<T>(string name) where T : class { return (T)win.FindName(name); }
        static string Tr(string s) { return L.T(s); }
        static string Tf(string s, params object[] a) { return L.F(s, a); }
        Brush Br(string k) { return (Brush)win.FindResource(k); }
        Style St(string k) { return (Style)win.FindResource(k); }
        FontFamily IconFont { get { return (FontFamily)win.FindResource("Icons"); } }
        static SolidColorBrush Hex(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }
        void Ui(Action a) { if (!closing) win.Dispatcher.BeginInvoke(a); }
        void Bg(Action a)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { a(); }
                catch (Exception ex) { Ui(() => { Log("Error: " + ex.Message); Toast("Error: " + ex.Message, 3); }); }
            });
        }

        TextBlock Txt(string t, double size, Brush fg, FontWeight? weight = null)
        {
            var tb = new TextBlock { Text = t, FontSize = size, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis };
            if (weight.HasValue) tb.FontWeight = weight.Value;
            return tb;
        }
        TextBlock Cap(string t) { return new TextBlock { Text = t, Style = St("Cap") }; }
        TextBlock Glyph(string g, double size, Brush fg)
        {
            var tb = new TextBlock { Text = g, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (fg != null) tb.Foreground = fg;
            return tb;
        }
        Border Badge(string text, string fg, string bg)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Background = Hex(bg), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Hex(fg) }
            };
        }
        Border IconBox(string glyph, string fg, string bg, double size = 34)
        {
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(9), Background = Hex(bg), Child = Glyph(glyph, size * 0.45, Hex(fg)), VerticalAlignment = VerticalAlignment.Top };
        }
        Button Btn(string text, string style, Action click)
        {
            var b = new Button { Content = text, Style = St(style), Padding = new Thickness(13, 7, 13, 7), FontSize = 12.5 };
            b.Click += (s, e) => click();
            return b;
        }

        FrameworkElement Row(string title, string desc, IEnumerable<UIElement> badges, FrameworkElement right, bool first, FrameworkElement left = null)
        {
            var g = new Grid { Margin = new Thickness(20, 14, 20, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (left != null) { left.Margin = new Thickness(0, 0, 14, 0); left.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(left); }
            var sp = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            var tl = new WrapPanel();
            tl.Children.Add(Txt(title, 13.5, Br("Text"), FontWeights.SemiBold));
            if (badges != null) foreach (var b in badges) tl.Children.Add(b);
            sp.Children.Add(tl);
            if (!string.IsNullOrEmpty(desc)) { var c = Cap(desc); c.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(c); }
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            if (right != null) { right.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(right, 2); g.Children.Add(right); }
            return new Border { BorderBrush = Br("Line"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Child = g };
        }

        static long NowSec() { return DateTime.Now.Ticks / TimeSpan.TicksPerSecond; }
        static DateTime FromSec(long s) { return new DateTime(s * TimeSpan.TicksPerSecond); }
        static string DateStr(long sec) { return FromSec(sec).ToString(L.En ? "MM/dd/yyyy HH:mm" : "dd/MM/yyyy HH:mm"); }

        // ============================================================ log / toast / tray
        readonly List<string> logLines = new List<string>();
        void Log(string msg)
        {
            if (!win.Dispatcher.CheckAccess()) { Ui(() => Log(msg)); return; }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            logLines.Add(line);
            var lb = E<TextBox>("LogBox");
            lb.AppendText((lb.Text.Length > 0 ? Environment.NewLine : "") + line);
            lb.ScrollToEnd();
        }

        void Toast(string msg, int kind, bool sticky = false)
        {
            if (!win.Dispatcher.CheckAccess()) { Ui(() => Toast(msg, kind, sticky)); return; }
            var t = E<Border>("Toast");
            E<TextBlock>("ToastText").Text = msg;
            var ic = E<TextBlock>("ToastIcon");
            ic.Text = kind == 0 ? "" : kind == 1 ? "" : kind == 2 ? "" : "";
            ic.Foreground = kind == 0 ? Br("Ok") : kind == 3 ? Br("Warn") : Br("Accent");
            t.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            if (toastTimer != null) toastTimer.Stop();
            if (sticky) return;
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(3.2, msg.Length / 18.0)) };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); t.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))); };
            toastTimer.Start();
        }

        void InitTray()
        {
            tray = new WF.NotifyIcon();
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Installer.CurrentExe); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            tray.Text = "GameBoost";
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add(Tr("Abrir GameBoost"), null, (s, e) => ShowWindow());
            trayModeItem = menu.Items.Add(Tr("Activar Modo Juego"), null, (s, e) => { if (sessionActive) EndSession(true); else StartSession(null, false, null); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(Tr("Salir"), null, (s, e) => ExitApp());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowWindow();
            tray.Visible = true;
        }

        void Balloon(string title, string text)
        {
            if (tray == null) return;
            if (!win.Dispatcher.CheckAccess()) { Ui(() => Balloon(title, text)); return; }
            try { tray.ShowBalloonTip(4000, title, string.IsNullOrEmpty(text) ? " " : text, WF.ToolTipIcon.Info); } catch { }
        }

        void ShowWindow()
        {
            win.Show();
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true; win.Topmost = false;
        }

        void ExitApp() { realExit = true; win.Close(); }

        static void RelaunchDelayed(string exe, string args)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & start \"\" \"" + exe + "\" " + args)
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        void OnWindowClosing(object s, CancelEventArgs e)
        {
            if (!realExit && settings.TrayOnClose && tray != null)
            {
                e.Cancel = true;
                win.Hide();
                if (!settings.TrayHintShown)
                {
                    Balloon(Tr("GameBoost sigue activo"), Tr("Está en la bandeja del sistema y activará el Modo Juego cuando abras un juego."));
                    settings.TrayHintShown = true;
                    Store.Save("settings.json", settings);
                }
                return;
            }
            closing = true;
            if (statTimer != null) statTimer.Stop();
            if (sessionActive)
            {
                SessionOps.StartServices(stoppedSvcs);
                if (timerHeld) { uint cur; Native.NtSetTimerResolution(5000, false, out cur); }
            }
            Store.Save("settings.json", settings);
            Store.Save("games.json", games);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Current.Shutdown();
        }

        // ============================================================ chrome / nav
        void WireChrome()
        {
            E<Grid>("TitleBar").MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
            E<Button>("BtnMin").Click += (s, e) => win.WindowState = WindowState.Minimized;
            E<Button>("BtnClose").Click += (s, e) => win.Close();
        }

        readonly string[] pageNames = { "Dash", "Games", "Tweaks", "Clean", "Bench", "Tips", "Apps", "Settings" };
        void WireNav()
        {
            foreach (var n in pageNames)
            {
                string name = n;
                E<RadioButton>("Nav" + name).Checked += (s, e) => ShowPage(name);
            }
        }
        void Nav(string name) { E<RadioButton>("Nav" + name).IsChecked = true; }
        void ShowPage(string name)
        {
            foreach (var n in pageNames)
            {
                var p = E<ScrollViewer>("Page" + n);
                if (n != name) { p.Visibility = Visibility.Collapsed; continue; }
                p.Visibility = Visibility.Visible;
                p.ScrollToTop();
                var tt = new TranslateTransform(0, 10);
                p.RenderTransform = tt;
                p.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            if (name == "Clean" && Cleaner.Items.All(i => i.Size < 0)) Analyze();
            if (name == "Apps") Bg(() => { var inst = Sys.InstalledApps(); Ui(() => { installedApps = inst; BuildApps(); }); });
        }

        // ============================================================ startup
        void StartupScan()
        {
            Bg(() =>
            {
                var info = SysInfo.Collect();
                var inst = Sys.InstalledApps();
                Ui(() =>
                {
                    sys = info; installedApps = inst;
                    ApplyProfile();
                    BuildTweaks();
                    if (statesLoaded) UpdateScore();
                    BuildSysList(); BuildProfileUi(); BuildTips(); BuildApps(); BuildRecs();
                    Log(Tr("Perfil detectado") + ": " + string.Join(" · ", Chips()));
                });
            });
            RefreshTweakStates();
            Bg(() =>
            {
                long j = 0;
                foreach (var id in new[] { "temp", "wintemp", "prefetch", "dumps", "recycle", "delivery" })
                {
                    var it = Cleaner.Get(id);
                    it.Size = Cleaner.Measure(it);
                    j += it.Size;
                }
                Ui(() => { junkBytes = j; UpdateCleanSizes(); BuildRecs(); });
            });
            ScanGames(false);
        }

        // ============================================================ hardware profile
        List<string> Chips()
        {
            var c = new List<string>();
            if (sys == null) return c;
            c.Add(Tr(sys.FormFactor));
            c.Add(sys.CpuVendor == "amd" ? (sys.DualX3D ? "AMD Ryzen X3D" : "AMD Ryzen") : sys.CpuVendor == "intel" ? (sys.IntelHybrid ? Tr("Intel híbrido (P+E)") : "Intel Core") : "CPU");
            c.Add(!sys.DedicatedGpu ? Tr("Gráficos integrados") : sys.GpuVendor == "nvidia" ? "NVIDIA GeForce" : sys.GpuVendor == "amd" ? "AMD Radeon" : sys.GpuVendor == "intel" ? "Intel Arc" : Tr("GPU dedicada"));
            if (sys.HybridGraphics) c.Add(Tr("Gráficos híbridos"));
            if (sys.Disk != "") c.Add(sys.Disk);
            if (sys.RamGB > 0) c.Add(sys.RamGB + " GB RAM");
            return c;
        }

        void ApplyProfile()
        {
            adaptations.Clear();
            if (sys.DualX3D)
            {
                var p = Tweaks.Get("power");
                p.Rec = false;
                p.Desc = Tf("No recomendado en tu {0}: los X3D de doble CCD necesitan core parking y el plan Equilibrado para llevar los juegos a los núcleos con 3D V-Cache.", sys.Cpu);
                adaptations.Add(Tr("CPU X3D de doble CCD: se mantiene el plan Equilibrado y no se cierra la Xbox Game Bar (el driver de V-Cache la necesita)."));
            }
            if (sys.IntelHybrid)
            {
                Tweaks.Get("throttle").Impact = 3;
                adaptations.Add(Tr("Intel con núcleos P y E: desactivar Power Throttling pasa a impacto alto, así el juego no termina en los E-cores."));
            }
            if (!sys.HagsSupported)
            {
                var h = Tweaks.Get("hags");
                h.Rec = false;
                string na = Tr("No disponible en tu gráfica.");
                if (!h.Desc.Contains(na)) h.Desc += " " + na;
                adaptations.Add(Tr("Programación de GPU por hardware fuera de las recomendaciones: tu gráfica no la soporta."));
            }
            if (!sys.DedicatedGpu)
            {
                Tweaks.Get("transp").Rec = true;
                adaptations.Add(Tr("Gráficos integrados: se recomienda quitar transparencias para liberar GPU y memoria compartida."));
            }
            if (sys.HybridGraphics) adaptations.Add(Tf("Gráficos híbridos: cada juego que abras con GameBoost se asigna a la {0}.", sys.Gpu));
            if (sys.HasBattery) adaptations.Add(Tr("Portátil: aviso automático si juegas con batería y consejos de energía específicos."));
            if (sys.IsHdd) adaptations.Add(Tr("Windows en disco duro (HDD): no se pausa SysMain ni se borra Prefetch, porque aceleran la carga en discos mecánicos."));
            if (sys.RamGB > 0 && sys.RamGB <= 8) adaptations.Add(Tf("{0} GB de RAM: liberación de RAM y cierre de apps activados en cada partida.", sys.RamGB));
            if (sys.GpuVendor == "nvidia") adaptations.Add(Tr("NVIDIA: consejos de Reflex y del Panel de control, revisión del driver y NVIDIA App en Apps recomendadas."));
            else if (sys.GpuVendor == "amd") adaptations.Add(Tr("AMD Radeon: consejos de Anti-Lag 2 y Adrenalin, revisión del driver y AMD Software en Apps recomendadas."));
            if (sys.CpuVendor == "amd") adaptations.Add(Tr("AMD Ryzen: se comprueban los drivers de chipset de AMD (mejoran el planificador y la gestión de energía)."));

            if (!settings.Adapted)
            {
                if (sys.DualX3D) settings.KillList = SessionOps.Without(settings.KillList, "GameBar");
                if (sys.IsHdd)
                {
                    settings.Services = SessionOps.Without(settings.Services, "SysMain");
                    cleanSwitches["prefetch"].IsChecked = false;
                    settings.CleanSel = Cleaner.Items.Where(i => cleanSwitches[i.Id].IsChecked == true).Select(i => i.Id).ToList();
                }
                settings.Adapted = true;
                Store.Save("settings.json", settings);
                E<TextBox>("KillBox").Text = settings.KillList;
                E<TextBox>("SvcBox").Text = settings.Services;
            }
        }

        WrapPanel ChipPanel(WrapPanel w)
        {
            w.Children.Clear();
            foreach (var c in Chips())
            {
                var b = Badge(c, "#C9C2FF", "#221C45");
                b.Margin = new Thickness(0, 0, 6, 6);
                b.Padding = new Thickness(9, 3, 9, 4);
                w.Children.Add(b);
            }
            return w;
        }

        void BuildProfileUi()
        {
            ChipPanel(E<WrapPanel>("SysChips"));
            ChipPanel(E<WrapPanel>("ProfileChips"));
            E<TextBlock>("ProfileTitle").Text = sys.Cpu + "  ·  " + sys.Gpu;
            var l = E<StackPanel>("ProfileList");
            l.Children.Clear();
            foreach (var a in adaptations)
            {
                var g = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var ic = Glyph("", 11, Hex("#A996FF")); ic.Margin = new Thickness(0, 3, 10, 0); ic.VerticalAlignment = VerticalAlignment.Top;
                g.Children.Add(ic);
                var t = Cap(a); t.Foreground = Hex("#B9BED0");
                Grid.SetColumn(t, 1); g.Children.Add(t);
                l.Children.Add(g);
            }
            E<Border>("ProfileBanner").Visibility = Visibility.Visible;
        }

        // ============================================================ dashboard
        void SetGreeting()
        {
            int h = DateTime.Now.Hour;
            E<TextBlock>("Greeting").Text = h >= 5 && h < 12 ? Tr("Buenos días") : h >= 12 && h < 20 ? Tr("Buenas tardes") : Tr("Buenas noches");
        }

        void WireDashboard()
        {
            E<Button>("BtnGameMode").Click += (s, e) => { if (sessionActive) EndSession(true); else StartSession(null, false, null); };
            E<Button>("BtnApplyRec").Click += (s, e) => ApplyRecommended();
            E<Button>("BtnApplyRec2").Click += (s, e) => ApplyRecommended();
            E<Button>("BtnRevertAll").Click += (s, e) => RevertAll();
            E<Button>("BtnRevertAll2").Click += (s, e) => RevertAll();
            E<Button>("BtnAllGames").Click += (s, e) => Nav("Games");
            E<Button>("UpdSkip").Click += (s, e) =>
            {
                if (pendingUpdate != null) { settings.SkipVersion = pendingUpdate.version; Store.Save("settings.json", settings); }
                E<Border>("UpdateBanner").Visibility = Visibility.Collapsed;
            };
            E<Button>("UpdGo").Click += (s, e) => InstallUpdate();
        }

        Border StatCard(string glyph, string title, string color, out TextBlock val, out TextBlock sub, out ScaleTransform bar, Button extra = null)
        {
            var card = new Border { Style = St("CardB"), Margin = new Thickness(6), Padding = new Thickness(18, 16, 18, 16) };
            var sp = new StackPanel();
            var head = new Grid();
            var hs = new StackPanel { Orientation = Orientation.Horizontal };
            hs.Children.Add(IconBox(glyph, color, "#1A1E29", 28));
            var tt = Txt(title, 12.5, Br("Sub")); tt.Margin = new Thickness(10, 0, 0, 0); tt.VerticalAlignment = VerticalAlignment.Center;
            hs.Children.Add(tt);
            head.Children.Add(hs);
            if (extra != null) { extra.HorizontalAlignment = HorizontalAlignment.Right; extra.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(extra); }
            sp.Children.Add(head);
            val = new TextBlock { Text = "—", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
            sp.Children.Add(val);
            sub = Cap(" "); sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.TextWrapping = TextWrapping.NoWrap;
            sp.Children.Add(sub);
            var track = new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 0) };
            track.Children.Add(new Border { Background = Hex("#1F2430"), CornerRadius = new CornerRadius(2) });
            bar = new ScaleTransform(0, 1);
            track.Children.Add(new Border { Background = Hex(color), CornerRadius = new CornerRadius(2), RenderTransform = bar });
            sp.Children.Add(track);
            card.Child = sp;
            return card;
        }

        void BuildStats()
        {
            var g = E<UniformGrid>("StatsGrid");
            var free = new Button { Style = St("Ghost"), Padding = new Thickness(8, 3, 8, 3), Content = new TextBlock { Text = Tr("Liberar"), FontSize = 12 } };
            free.Click += (s, e) => Bg(() => { Sys.PurgeStandby(); Toast(Tr("Memoria en espera (standby) liberada"), 0); Log(Tr("Memoria en espera (standby) liberada")); });
            g.Children.Add(StatCard("", Tr("Procesador"), "#7B61FF", out cpuVal, out cpuSub, out cpuBar));
            g.Children.Add(StatCard("", Tr("Memoria RAM"), "#5A7BFF", out ramVal, out ramSub, out ramBar, free));
            g.Children.Add(StatCard("", Tr("Latencia de red"), "#22C55E", out pingVal, out pingSub, out pingBar));
            g.Children.Add(StatCard("", Tr("Disco del sistema"), "#F5A524", out diskVal, out diskSub, out diskBar));
            cpuSub.Text = Tf("{0} hilos", Environment.ProcessorCount);
            pingSub.Text = Tr("Midiendo...");
        }

        void Anim(ScaleTransform t, double v)
        {
            t.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Max(0, Math.Min(1, v)), TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase() });
        }

        int diskTick;
        void UpdateStats()
        {
            if (!win.IsVisible && !sessionActive) return;
            long idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                if (lastKernel != 0)
                {
                    long di = idle - lastIdle, dt = (kernel - lastKernel) + (user - lastUser);
                    double pct = dt > 0 ? 1.0 - (double)di / dt : 0;
                    cpuVal.Text = Math.Round(pct * 100) + "%";
                    Anim(cpuBar, pct);
                }
                lastIdle = idle; lastKernel = kernel; lastUser = user;
            }
            var m = new Native.MEMORYSTATUSEX();
            if (Native.GlobalMemoryStatusEx(m))
            {
                double tot = m.ullTotalPhys / 1073741824.0, used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                ramVal.Text = m.dwMemoryLoad + "%";
                ramSub.Text = Tf("{0} de {1} GB en uso", used.ToString("0.0"), tot.ToString("0"));
                Anim(ramBar, m.dwMemoryLoad / 100.0);
            }
            if (diskTick++ % 20 == 0)
            {
                try
                {
                    var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                    diskVal.Text = Sys.Fmt(d.AvailableFreeSpace);
                    diskSub.Text = Tf("libres de {0} en {1}", Sys.Fmt(d.TotalSize), d.Name.TrimEnd('\\')) + (sys != null && sys.Disk != "" ? " · " + sys.Disk : "");
                    Anim(diskBar, 1 - (double)d.AvailableFreeSpace / d.TotalSize);
                }
                catch { }
            }
            if (sessionActive)
            {
                var mins = (int)(DateTime.Now - sessionStart).TotalMinutes;
                E<TextBlock>("SessSub").Text = (sessionName != null ? sessionName + " · " : "") + (mins < 1 ? Tr("recién activado") : mins + " min");
            }
        }

        void PingLoop()
        {
            var samples = new List<long>();
            while (!closing)
            {
                if (!win.Dispatcher.Invoke(() => win.IsVisible)) { Thread.Sleep(3000); continue; }
                long ms = -1;
                try { using (var p = new Ping()) { var r = p.Send("1.1.1.1", 1500); if (r.Status == IPStatus.Success) ms = r.RoundtripTime; } } catch { }
                if (ms >= 0) { samples.Add(ms); if (samples.Count > 10) samples.RemoveAt(0); }
                long jitter = samples.Count > 1 ? (long)samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
                long v = ms;
                Ui(() =>
                {
                    if (v < 0) { pingVal.Text = Tr("Sin red"); pingSub.Text = Tr("No hay respuesta de 1.1.1.1"); Anim(pingBar, 1); return; }
                    pingVal.Text = v + " ms";
                    pingSub.Text = "Jitter " + jitter + " ms · " + (v < 30 ? Tr("Excelente") : v < 60 ? Tr("Buena") : v < 100 ? Tr("Regular") : Tr("Alta"));
                    Anim(pingBar, Math.Min(1, v / 150.0));
                });
                Thread.Sleep(3000);
            }
        }

        double ringShown;
        void SetRing(double pct, bool animate)
        {
            var path = E<Shapes.Path>("RingArc");
            if (!animate) { DrawArc(path, pct); ringShown = pct; return; }
            double from = ringShown; int step = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            t.Tick += (s, e) =>
            {
                step++;
                double k = Math.Min(1, step / 40.0); k = 1 - Math.Pow(1 - k, 3);
                double v = from + (pct - from) * k;
                DrawArc(path, v);
                E<TextBlock>("ScoreText").Text = Math.Round(v * 100) + "%";
                if (step >= 40) { t.Stop(); ringShown = pct; }
            };
            t.Start();
        }
        static void DrawArc(Shapes.Path path, double pct)
        {
            double r = 61, c = 66;
            pct = Math.Max(0.0005, Math.Min(0.9995, pct));
            double ang = pct * 360, rad = (ang - 90) * Math.PI / 180;
            var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Cos(rad), c + r * Math.Sin(rad)), new Size(r, r), 0, ang > 180, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            path.Data = geo;
        }

        void UpdateScore()
        {
            var rec = Tweaks.All.Where(t => t.Rec).ToList();
            int on = rec.Count(t => t.State);
            double pct = rec.Count == 0 ? 1 : (double)on / rec.Count;
            SetRing(pct, true);
            E<TextBlock>("ScoreSub").Text = Tf("{0} de {1} optimizaciones recomendadas activas", on, rec.Count);
            E<Button>("BtnApplyRec").Visibility = on < rec.Count ? Visibility.Visible : Visibility.Collapsed;
            E<TextBlock>("GreetingSub").Text = pct >= 1 ? Tr("Tu PC está lista para jugar.") : Tf("Tu PC está optimizada al {0}%. Activa lo que falta con un clic.", Math.Round(pct * 100));
            E<TextBlock>("TweaksSub").Text = Tf("{0} de {1} ajustes activos · Todo es reversible con un clic.", Tweaks.All.Count(t => t.State), Tweaks.All.Count);
            foreach (var cat in Tweaks.Cats)
            {
                TextBlock tb;
                if (!catCounters.TryGetValue(cat, out tb)) continue;
                var l = Tweaks.All.Where(t => t.Cat == cat).ToList();
                tb.Text = Tf("{0}/{1} activos", l.Count(t => t.State), l.Count);
            }
            UpdateBackupInfo();
        }

        void BuildSysList()
        {
            var l = E<StackPanel>("SysList");
            l.Children.Clear();
            ulong up = Native.GetTickCount64() / 1000;
            string upt = up > 86400 ? (up / 86400) + " d " + (up % 86400 / 3600) + " h" : (up / 3600) + " h " + (up % 3600 / 60) + " min";
            string drv = sys.GpuDriver == "" ? "—" : sys.GpuDriver + (sys.DriverDate.HasValue ? " · " + sys.DriverDate.Value.ToString("MM/yyyy") : "");
            var rows = new[]
            {
                new[] { "", Tr("Procesador"), sys.Cpu },
                new[] { "", Tr("Gráfica"), sys.Gpu },
                new[] { "", Tr("Driver gráfico"), drv },
                new[] { "", Tr("Memoria"), sys.Ram },
                new[] { "", Tr("Pantalla"), sys.Display + (sys.MaxHz > sys.Hz ? "  (" + Tf("máx. {0} Hz", sys.MaxHz) + ")" : "") },
                new[] { "", Tr("Sistema"), sys.Os },
                new[] { "", Tr("Encendido"), upt },
            };
            bool first = true;
            foreach (var r in rows)
            {
                var g = new Grid { Margin = new Thickness(20, 9, 20, 9) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var ic = Glyph(r[0], 14, Br("Mute")); ic.Margin = new Thickness(0, 1, 12, 0); ic.VerticalAlignment = VerticalAlignment.Top;
                g.Children.Add(ic);
                var sp = new StackPanel();
                sp.Children.Add(Txt(r[1], 11.5, Br("Sub")));
                var v = Txt(r[2], 13, Br("Text")); v.ToolTip = r[2];
                sp.Children.Add(v);
                Grid.SetColumn(sp, 1); g.Children.Add(sp);
                l.Children.Add(new Border { BorderBrush = Br("Line"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Child = g });
                first = false;
            }
        }

        bool IsInstalled(string key)
        {
            return key != null && installedApps != null && installedApps.Any(n => n.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        string DriverUrl() { return sys.GpuVendor == "nvidia" ? UrlNvidia : sys.GpuVendor == "amd" ? UrlAmdGpu : UrlIntel; }

        void BuildRecs()
        {
            var recs = new List<Rec>();
            if (statesLoaded)
            {
                var pending = Tweaks.All.Where(t => t.Rec && !t.State).ToList();
                if (pending.Count > 0)
                    recs.Add(new Rec
                    {
                        Sev = pending.Any(t => t.Impact == 3) ? 3 : 2,
                        Title = pending.Count == 1 ? Tr("1 optimización recomendada sin aplicar") : Tf("{0} optimizaciones recomendadas sin aplicar", pending.Count),
                        Desc = string.Join(", ", pending.Take(4).Select(t => t.Title)) + (pending.Count > 4 ? " " + Tf("y {0} más.", pending.Count - 4) : "."),
                        Btn = Tr("Aplicar"), Do = ApplyRecommended
                    });
            }
            if (rebootPending)
                recs.Add(new Rec { Sev = 2, Title = Tr("Reinicia para completar los cambios"), Desc = Tr("Algunos ajustes (programación de GPU, timer, teclas especiales) se activan tras reiniciar.") });
            if (sys != null)
            {
                if (sys.MaxHz > sys.Hz && sys.Hz > 0)
                    recs.Add(new Rec { Sev = 3, Title = Tf("Tu pantalla está a {0} Hz pero soporta {1} Hz", sys.Hz, sys.MaxHz), Desc = Tr("Estás perdiendo fluidez. Cámbialo en Configuración > Pantalla > Pantalla avanzada > Frecuencia de actualización."), Btn = Tr("Abrir"), Do = () => Open("ms-settings:display-advanced") });
                if (sys.OnBattery)
                    recs.Add(new Rec { Sev = 3, Title = Tr("Estás usando la batería"), Desc = Tr("En batería la CPU y la GPU bajan su rendimiento. Conecta el cargador para jugar.") });
                if (sys.DriverDate.HasValue && sys.DedicatedGpu)
                {
                    int months = (int)((DateTime.Now - sys.DriverDate.Value).TotalDays / 30);
                    if (months >= 5)
                        recs.Add(new Rec
                        {
                            Sev = 2, Title = Tf("Driver de gráficos de hace {0} meses", months),
                            Desc = Tf("Tienes el driver {0}. Los drivers nuevos traen más FPS y correcciones para juegos recientes.", sys.GpuDriver),
                            Btn = Tr("Actualizar"), Do = () => Open(DriverUrl())
                        });
                }
                if (sys.CpuVendor == "amd" && installedApps != null && !IsInstalled("AMD Chipset Software"))
                    recs.Add(new Rec { Sev = sys.DualX3D ? 3 : 1, Title = Tr("Instala los drivers de chipset de AMD"), Desc = sys.DualX3D ? Tf("Imprescindibles en tu {0}: incluyen el driver que envía los juegos a los núcleos con 3D V-Cache.", sys.Cpu) : Tr("Mejoran el planificador de núcleos y la gestión de energía de tu Ryzen."), Btn = Tr("Descargar"), Do = () => Open(UrlAmdDrivers) });
                if (sys.Startup > 6)
                    recs.Add(new Rec { Sev = 1, Title = Tf("{0} programas se inician con Windows", sys.Startup), Desc = Tr("Desactiva los que no uses (launchers, actualizadores, Spotify...) para tener más RAM y CPU libres."), Btn = Tr("Gestionar"), Do = () => Open("taskmgr", "/0 /startup") });
                if ((sys.RamType == 26 && sys.RamMHz > 0 && sys.RamMHz <= 2666) || (sys.RamType == 34 && sys.RamMHz > 0 && sys.RamMHz <= 4800 && !sys.HasBattery))
                    recs.Add(new Rec { Sev = 1, Title = Tf("Tu RAM funciona a {0} MT/s", sys.RamMHz), Desc = Tr("Si tu memoria es de mayor velocidad, activa el perfil XMP/EXPO en la BIOS: puede dar un 5-15% más de FPS en juegos que dependen de la CPU.") });
                if (sys.RamGB > 0 && sys.RamGB < 12)
                    recs.Add(new Rec { Sev = 1, Title = Tf("Tienes {0} GB de RAM", sys.RamGB), Desc = Tr("Los juegos actuales piden 16 GB. Cierra el navegador antes de jugar; el Modo Juego ya cierra las apps en segundo plano.") });
                if (sys.IsHdd)
                    recs.Add(new Rec { Sev = 2, Title = Tr("Windows está en un disco duro (HDD)"), Desc = Tr("Un SSD es la mejora más notable que puedes hacer: cargas 5-10 veces más rápidas y menos tirones.") });
            }
            if (junkBytes > 300L * 1024 * 1024)
                recs.Add(new Rec { Sev = 2, Title = Tf("{0} en archivos basura", Sys.Fmt(junkBytes)), Desc = Tr("Temporales, prefetch, volcados de errores, papelera y caché de actualizaciones."), Btn = Tr("Limpiar"), Do = QuickClean });
            ulong upDays = Native.GetTickCount64() / 86400000;
            if (upDays >= 3)
                recs.Add(new Rec { Sev = 2, Title = Tf("Tu PC lleva {0} días encendida", upDays), Desc = Tr("Reiniciar libera memoria, cierra procesos colgados y aplica actualizaciones pendientes. El 'Inicio rápido' no cuenta como reinicio.") });
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if ((double)d.AvailableFreeSpace / d.TotalSize < 0.12)
                    recs.Add(new Rec { Sev = 3, Title = Tf("Poco espacio en {0} ({1} libres)", d.Name.TrimEnd('\\'), Sys.Fmt(d.AvailableFreeSpace)), Desc = Tr("Con menos del 10-15% libre el SSD se vuelve más lento y Windows no puede actualizar bien."), Btn = Tr("Limpiar"), Do = () => Nav("Clean") });
            }
            catch { }
            if (bench.Items.Count == 0)
                recs.Add(new Rec { Sev = 1, Title = Tr("Mide el rendimiento de tu PC"), Desc = Tr("Ejecuta un benchmark para tener una referencia y comparar después de optimizar."), Btn = Tr("Abrir"), Do = () => Nav("Bench") });
            if (recs.Count == 0)
                recs.Add(new Rec { Sev = 0, Title = Tr("Todo en orden"), Desc = Tr("Tu PC está optimizada. Pulsa Jugar en cualquier juego y GameBoost hará el resto.") });

            recs = recs.OrderByDescending(r => r.Sev).ToList();
            var list = E<StackPanel>("RecsList");
            list.Children.Clear();
            int pend = recs.Count(r => r.Sev > 0);
            E<TextBlock>("RecsCount").Text = pend > 0 ? Tf("{0} pendientes", pend) : "";
            bool first = true;
            foreach (var r in recs)
            {
                string fg = r.Sev == 3 ? "#F5A524" : r.Sev == 2 ? "#9D8CFF" : r.Sev == 1 ? "#7FA2FF" : "#22C55E";
                string bg = r.Sev == 3 ? "#2A2113" : r.Sev == 2 ? "#1E1A38" : r.Sev == 1 ? "#161E33" : "#11261A";
                string gl = r.Sev == 3 ? "" : r.Sev == 2 ? "" : r.Sev == 1 ? "" : "";
                FrameworkElement btn = null;
                if (r.Do != null) { var a = r.Do; btn = Btn(r.Btn, "Secondary", () => a()); }
                list.Children.Add(Row(r.Title, r.Desc, null, btn, first, IconBox(gl, fg, bg)));
                first = false;
            }
        }

        void Open(string target, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true }); } catch (Exception ex) { Toast(Tr("No se pudo abrir") + ": " + ex.Message, 3); }
        }

        // ============================================================ updates
        void CheckUpdates(bool manual)
        {
            if (manual) Toast(Tr("Buscando actualizaciones..."), 2, true);
            Bg(() =>
            {
                UpdateInfo u = null; string err = null;
                try { u = Updater.Check(); } catch (Exception ex) { err = ex.Message; }
                Ui(() =>
                {
                    if (u != null && (manual || u.version != settings.SkipVersion))
                    {
                        pendingUpdate = u;
                        E<TextBlock>("UpdTitle").Text = Tf("GameBoost {0} disponible", u.version);
                        E<TextBlock>("UpdNotes").Text = string.IsNullOrEmpty(u.notes) ? Tr("Incluye mejoras y correcciones.") : u.notes;
                        E<Border>("UpdateBanner").Visibility = Visibility.Visible;
                        if (manual) { Toast(Tf("GameBoost {0} disponible", u.version), 0); Nav("Dash"); }
                        if (!win.IsVisible) Balloon(Tf("GameBoost {0} disponible", u.version), Tr("Abre GameBoost para actualizar."));
                        Log(Tf("GameBoost {0} disponible", u.version));
                    }
                    else if (manual)
                        Toast(err != null ? Tr("No se pudo conectar con el servidor de actualizaciones") : Tf("Ya tienes la última versión ({0})", Installer.Version), err != null ? 3 : 0);
                });
            });
        }

        void InstallUpdate()
        {
            var u = pendingUpdate;
            if (u == null) return;
            E<Button>("UpdGo").IsEnabled = false;
            Bg(() =>
            {
                try
                {
                    string f = Updater.Download(u, p => Ui(() => E<TextBlock>("UpdNotes").Text = Tf("Descargando... {0}%", p)));
                    Log(Tf("Instalando GameBoost {0}", u.version));
                    Process.Start(new ProcessStartInfo(f, "--update") { UseShellExecute = true });
                    Ui(ExitApp);
                }
                catch (Exception ex)
                {
                    Ui(() => { E<Button>("UpdGo").IsEnabled = true; E<TextBlock>("UpdNotes").Text = Tr("No se pudo descargar la actualización") + ": " + ex.Message; });
                }
            });
        }

        // ============================================================ quick play
        void RebuildQuick()
        {
            var g = E<UniformGrid>("QuickGrid");
            g.Children.Clear();
            var list = games.Games.OrderByDescending(x => x.LastPlayed).ThenByDescending(x => x.Plays).ThenBy(x => x.Name).Take(4).ToList();
            if (list.Count == 0)
            {
                var b = new Border { Style = St("CardB"), Margin = new Thickness(6), Padding = new Thickness(18) };
                b.Child = Cap(Tr("Aún no hay juegos. Ve a Juegos y pulsa 'Buscar juegos' o 'Agregar juego'."));
                g.Columns = 1;
                g.Children.Add(b);
                return;
            }
            g.Columns = 4;
            foreach (var game in list)
            {
                var gm = game;
                var tile = new Border { Height = 96, Margin = new Thickness(6), CornerRadius = new CornerRadius(12), Background = HeaderBrush(gm), Cursor = Cursors.Hand, BorderBrush = Br("Line"), BorderThickness = new Thickness(1), ClipToBounds = true };
                var grid = new Grid();
                if (string.IsNullOrEmpty(gm.Image) || !File.Exists(gm.Image)) AddIconCenter(grid, gm, 30, new Thickness(0, 0, 0, 22));
                grid.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(215, 8, 10, 14), 90) });
                var name = Txt(gm.Name, 13, Brushes.White, FontWeights.SemiBold);
                name.VerticalAlignment = VerticalAlignment.Bottom; name.Margin = new Thickness(12, 0, 52, 11);
                grid.Children.Add(name);
                var play = new Button { Style = St("Round"), Content = "", Width = 32, Height = 32, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 9) };
                play.Click += (s, e) => Play(gm);
                grid.Children.Add(play);
                tile.Child = grid;
                tile.MouseEnter += (s, e) => tile.BorderBrush = Hex("#4A3D99");
                tile.MouseLeave += (s, e) => tile.BorderBrush = Br("Line");
                g.Children.Add(tile);
            }
        }

        // ============================================================ games
        static readonly string[][] Palettes =
        {
            new[] { "#3B2A7A", "#1B2B5E" }, new[] { "#5A1E3A", "#2A1B4E" }, new[] { "#0F4C45", "#15304F" },
            new[] { "#4A2A0F", "#3A1640" }, new[] { "#1E3A5F", "#2B1B55" }, new[] { "#2D3748", "#1A202C" }
        };
        Brush HeaderBrush(Game g)
        {
            if (!string.IsNullOrEmpty(g.Image) && File.Exists(g.Image))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.UriSource = new Uri(g.Image); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 560; bi.EndInit(); bi.Freeze();
                    return new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                }
                catch { }
            }
            var p = Palettes[Math.Abs((g.Name ?? "").GetHashCode()) % Palettes.Length];
            return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(p[0]), (Color)ColorConverter.ConvertFromString(p[1]), 35);
        }

        ImageSource GetIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            ImageSource s;
            if (iconCache.TryGetValue(path, out s)) return s;
            try
            {
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    s = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    s.Freeze();
                }
            }
            catch { s = null; }
            iconCache[path] = s;
            return s;
        }

        void AddIconCenter(Grid grid, Game g, double size, Thickness margin)
        {
            var ic = GetIcon(g.Icon) ?? GetIcon(File.Exists(g.Target ?? "") ? g.Target : null);
            if (ic != null)
            {
                var img = new Image { Source = ic, Width = size, Height = size, Margin = margin, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
            }
            else
            {
                string ini = string.Concat((g.Name ?? "?").Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpper(x[0])));
                grid.Children.Add(new TextBlock { Text = ini, FontSize = size * 1.1, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = margin });
            }
        }

        void WireGames()
        {
            E<Button>("BtnScan").Click += (s, e) => ScanGames(true);
            E<Button>("BtnAddGame").Click += (s, e) => OpenEditor(null);
            E<Button>("EdCancel").Click += (s, e) => CloseEditor();
            E<Button>("EdSave").Click += (s, e) => SaveEditor();
            E<Button>("EdBrowse").Click += (s, e) =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = Tr("Programas") + " (*.exe)|*.exe|" + Tr("Todos los archivos") + " (*.*)|*.*", Title = Tr("Selecciona el ejecutable del juego") };
                if (d.ShowDialog(win) != true) return;
                E<TextBox>("EdTarget").Text = d.FileName;
                if (E<TextBox>("EdProc").Text.Trim() == "") E<TextBox>("EdProc").Text = Path.GetFileNameWithoutExtension(d.FileName);
                if (E<TextBox>("EdName").Text.Trim() == "") E<TextBox>("EdName").Text = Path.GetFileNameWithoutExtension(d.FileName);
            };
            E<Button>("EdDelete").Click += (s, e) =>
            {
                if (editing == null) return;
                if (!deleteArmed) { deleteArmed = true; E<TextBlock>("EdDeleteText").Text = Tr("¿Seguro? Pulsa otra vez"); return; }
                Sys.Ifeo(editing.Proc, "Normal");
                games.Games.Remove(editing);
                Store.Save("games.json", games);
                Toast(Tr("Juego eliminado") + ": " + editing.Name, 1);
                CloseEditor(); RebuildGames(); RebuildQuick();
            };
            E<Grid>("Overlay").MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == E<Grid>("Overlay")) CloseEditor(); };
        }

        void ScanGames(bool manual)
        {
            if (manual) Toast(Tr("Buscando juegos en Steam, Epic y Riot..."), 2, true);
            Bg(() =>
            {
                var found = Detect.All();
                Ui(() =>
                {
                    int added = 0;
                    foreach (var f in found)
                    {
                        var ex = games.Games.FirstOrDefault(x => x.Id == f.Id);
                        if (ex == null) { games.Games.Add(f); added++; continue; }
                        ex.Target = f.Target; ex.Image = f.Image ?? ex.Image; ex.Icon = f.Icon ?? ex.Icon; ex.Source = f.Source;
                        if (string.IsNullOrEmpty(ex.Proc)) ex.Proc = f.Proc;
                        if (string.IsNullOrEmpty(ex.Args)) ex.Args = f.Args;
                    }
                    Store.Save("games.json", games);
                    RebuildGames(); RebuildQuick();
                    if (manual) Toast(Tf("{0} juegos detectados", found.Count) + (added > 0 ? " · " + Tf("{0} nuevos", added) : ""), 0);
                    if (added > 0) Log(Tf("{0} juegos nuevos detectados", added));
                });
            });
        }

        void RebuildGames()
        {
            var w = E<WrapPanel>("GamesWrap");
            w.Children.Clear();
            var list = games.Games.OrderByDescending(x => x.LastPlayed).ThenBy(x => x.Name).ToList();
            E<TextBlock>("GamesSub").Text = list.Count == 0 ? Tr("No hay juegos todavía. Búscalos automáticamente o agrégalos a mano.") :
                (list.Count == 1 ? Tr("1 juego") : Tf("{0} juegos", list.Count)) + " · " + Tr("Pulsa ▶ y GameBoost optimiza la PC, cierra lo que sobra y abre el juego.");
            foreach (var g in list) w.Children.Add(GameCard(g));

            var add = new Grid { Width = 270, Height = 222, Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var rect = new Shapes.Rectangle { RadiusX = 12, RadiusY = 12, Stroke = Hex("#2A3040"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
            add.Children.Add(rect);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Glyph("", 22, Br("Sub")));
            var t = Txt(Tr("Agregar juego"), 13, Br("Sub")); t.Margin = new Thickness(0, 10, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            add.Children.Add(sp);
            add.MouseEnter += (s, e) => rect.Stroke = Br("Accent");
            add.MouseLeave += (s, e) => rect.Stroke = Hex("#2A3040");
            add.MouseLeftButtonUp += (s, e) => OpenEditor(null);
            w.Children.Add(add);
        }

        FrameworkElement GameCard(Game g)
        {
            var card = new Border { Width = 270, Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(12), Background = Br("Card"), BorderBrush = Br("Line"), BorderThickness = new Thickness(1) };
            var sp = new StackPanel();
            var head = new Border { Height = 126, CornerRadius = new CornerRadius(11, 11, 0, 0), Background = HeaderBrush(g) };
            var hg = new Grid();
            if (string.IsNullOrEmpty(g.Image) || !File.Exists(g.Image)) AddIconCenter(hg, g, 40, new Thickness(0));
            string srcCol = g.Source == "Steam" ? "#66C0F4" : g.Source == "Epic" ? "#E8EAF0" : g.Source == "Riot" ? "#FF6B6B" : "#B9A8FF";
            hg.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Background = new SolidColorBrush(Color.FromArgb(190, 10, 12, 18)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10),
                Child = new TextBlock { Text = g.Source == "Manual" || g.Source == null ? Tr("Manual") : g.Source, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Hex(srcCol) }
            });
            head.Child = hg;
            sp.Children.Add(head);

            var bottom = new Grid { Margin = new Thickness(16, 13, 12, 14) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Txt(g.Name, 14, Br("Text"), FontWeights.SemiBold));
            string sub = g.LastPlayed > 0 ? Tr("Jugado") + " " + Ago(FromSec(g.LastPlayed)) : (string.IsNullOrEmpty(g.Proc) ? Tr("Sin proceso configurado") : g.Proc + ".exe");
            var st = Txt(sub, 12, Br("Sub")); st.Margin = new Thickness(0, 2, 0, 0);
            info.Children.Add(st);
            bottom.Children.Add(info);
            var edit = new Button { Style = St("Ghost"), Content = Glyph("", 13, null), Margin = new Thickness(0, 0, 6, 0), ToolTip = Tr("Editar") };
            edit.Click += (s, e) => OpenEditor(g);
            Grid.SetColumn(edit, 1); bottom.Children.Add(edit);
            var play = new Button { Style = St("Round"), Content = "", ToolTip = Tr("Optimizar y jugar") };
            play.Click += (s, e) => Play(g);
            Grid.SetColumn(play, 2); bottom.Children.Add(play);
            sp.Children.Add(bottom);
            card.Child = sp;
            card.MouseEnter += (s, e) => card.BorderBrush = Hex("#3A3170");
            card.MouseLeave += (s, e) => card.BorderBrush = Br("Line");
            return card;
        }

        static string Ago(DateTime d)
        {
            var t = DateTime.Now - d;
            if (t.TotalMinutes < 60) return Tf("hace {0} min", Math.Max(1, (int)t.TotalMinutes));
            if (t.TotalHours < 24) return Tf("hace {0} h", (int)t.TotalHours);
            if (t.TotalDays < 2) return Tr("ayer");
            return Tf("hace {0} días", (int)t.TotalDays);
        }

        void OpenEditor(Game g)
        {
            editing = g; deleteArmed = false;
            E<TextBlock>("EdDeleteText").Text = Tr("Eliminar juego");
            E<Button>("EdDelete").Visibility = g == null ? Visibility.Collapsed : Visibility.Visible;
            E<TextBlock>("EdTitle").Text = g == null ? Tr("Agregar juego") : Tr("Editar") + " " + g.Name;
            E<TextBox>("EdName").Text = g == null ? "" : g.Name;
            E<TextBox>("EdTarget").Text = g == null ? "" : g.Target;
            E<TextBox>("EdArgs").Text = g == null ? "" : g.Args;
            E<TextBox>("EdProc").Text = g == null ? "" : g.Proc;
            string p = g == null ? "High" : (g.Priority ?? "High");
            E<RadioButton>(p == "Normal" ? "EdPrioN" : p == "AboveNormal" ? "EdPrioA" : "EdPrioH").IsChecked = true;
            var ov = E<Grid>("Overlay");
            ov.Visibility = Visibility.Visible;
            ov.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            E<TextBox>("EdName").Focus();
        }
        void CloseEditor() { E<Grid>("Overlay").Visibility = Visibility.Collapsed; editing = null; }

        void SaveEditor()
        {
            string name = E<TextBox>("EdName").Text.Trim(), target = E<TextBox>("EdTarget").Text.Trim();
            if (name == "" || target == "") { Toast(Tr("Pon al menos el nombre y el ejecutable o URL"), 3); return; }
            var g = editing;
            if (g == null) { g = new Game { Id = "manual:" + Guid.NewGuid().ToString("N"), Source = "Manual" }; games.Games.Add(g); }
            string oldProc = g.Proc;
            g.Name = name; g.Target = target;
            g.Args = E<TextBox>("EdArgs").Text.Trim();
            string proc = E<TextBox>("EdProc").Text.Trim();
            if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) proc = proc.Substring(0, proc.Length - 4);
            g.Proc = proc;
            g.Priority = E<RadioButton>("EdPrioN").IsChecked == true ? "Normal" : E<RadioButton>("EdPrioA").IsChecked == true ? "AboveNormal" : "High";
            if (g.Icon == null && File.Exists(target)) g.Icon = target;
            if (!string.IsNullOrEmpty(oldProc) && oldProc != proc) Sys.Ifeo(oldProc, "Normal");
            Sys.Ifeo(g.Proc, g.Priority);
            Store.Save("games.json", games);
            CloseEditor(); RebuildGames(); RebuildQuick();
            Toast(Tr("Guardado") + ": " + g.Name, 0);
        }

        // ============================================================ tweaks
        void BuildTweaks()
        {
            var list = E<StackPanel>("TweaksList");
            list.Children.Clear(); tweakSwitches.Clear(); catCounters.Clear();
            foreach (var cat in Tweaks.Cats)
            {
                list.Children.Add(new TextBlock { Text = Tr(cat).ToUpperInvariant(), Style = St("Sec") });
                var counter = Cap(""); counter.HorizontalAlignment = HorizontalAlignment.Right; counter.Margin = new Thickness(0, -27, 4, 10);
                catCounters[cat] = counter;
                list.Children.Add(counter);
                var card = new Border { Style = St("CardB"), Padding = new Thickness(0) };
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                bool first = true;
                foreach (var t in Tweaks.All.Where(x => x.Cat == cat))
                {
                    var tw = t;
                    var badges = new List<UIElement>();
                    if (tw.Impact == 3) badges.Add(Badge(Tr("Impacto alto"), "#F5B04B", "#2A2113"));
                    else if (tw.Impact == 2) badges.Add(Badge(Tr("Impacto medio"), "#8FA8FF", "#172036"));
                    else badges.Add(Badge(Tr("Impacto bajo"), "#8A90A2", "#1A1E29"));
                    if (tw.Rec) badges.Add(Badge(Tr("Recomendado"), "#B3A3FF", "#211B40"));
                    if (tw.Reboot) badges.Add(Badge(Tr("Requiere reinicio"), "#8A90A2", "#1A1E29"));
                    var sw = new CheckBox { Style = St("Switch"), IsChecked = tw.State, IsEnabled = statesLoaded };
                    sw.Click += (s, e) => ToggleTweak(tw, sw);
                    tweakSwitches[tw.Id] = sw;
                    sp.Children.Add(Row(tw.Title, tw.Desc, badges, sw, first));
                    first = false;
                }
                card.Child = sp;
                list.Children.Add(card);
            }
        }

        void ToggleTweak(Tweak t, CheckBox sw)
        {
            bool want = sw.IsChecked == true;
            sw.IsEnabled = false;
            Bg(() =>
            {
                string err = null;
                try { if (want) t.Apply(); else t.Revert(); } catch (Exception ex) { err = ex.Message; }
                bool now = t.SafeCheck();
                Ui(() =>
                {
                    t.State = now; sw.IsChecked = now; sw.IsEnabled = true;
                    if (err != null) { Toast(Tf("No se pudo cambiar '{0}'", t.Title) + ": " + err, 3); Log("Error: " + t.Title + ": " + err); }
                    else
                    {
                        if (t.Reboot) rebootPending = true;
                        Toast((want ? Tr("Activado") : Tr("Desactivado")) + ": " + t.Title + (t.Reboot ? " · " + Tr("requiere reinicio") : ""), want ? 0 : 1);
                        Log((want ? Tr("Activado") : Tr("Desactivado")) + ": " + t.Title);
                    }
                    UpdateScore(); BuildRecs();
                });
            });
        }

        void RefreshTweakStates()
        {
            Bg(() =>
            {
                var states = Tweaks.All.ToDictionary(t => t.Id, t => t.SafeCheck());
                Ui(() =>
                {
                    statesLoaded = true;
                    foreach (var t in Tweaks.All)
                    {
                        t.State = states[t.Id];
                        CheckBox sw;
                        if (tweakSwitches.TryGetValue(t.Id, out sw)) { sw.IsChecked = t.State; sw.IsEnabled = true; }
                    }
                    UpdateScore(); BuildRecs();
                });
            });
        }

        void ApplyRecommended()
        {
            var todo = Tweaks.All.Where(t => t.Rec && !t.State).ToList();
            if (todo.Count == 0) { Toast(Tr("Ya tienes todas las optimizaciones recomendadas"), 0); return; }
            Toast(Tf("Aplicando {0} optimizaciones...", todo.Count), 2, true);
            Bg(() =>
            {
                int ok = 0;
                foreach (var t in todo) { try { t.Apply(); ok++; if (t.Reboot) rebootPending = true; } catch (Exception ex) { Log("Error: " + t.Title + ": " + ex.Message); } }
                Log(Tf("{0} optimizaciones aplicadas", ok));
                Ui(() => Toast(Tf("{0} optimizaciones aplicadas", ok) + (rebootPending ? " · " + Tr("reinicia para completar") : ""), 0));
                RefreshTweakStates();
            });
        }

        void RevertAll()
        {
            var todo = Tweaks.All.Where(t => t.State).ToList();
            if (todo.Count == 0) { Toast(Tr("No hay ajustes que restaurar"), 1); return; }
            Toast(Tr("Restaurando valores de Windows..."), 2, true);
            Bg(() =>
            {
                foreach (var t in todo) { try { t.Revert(); } catch (Exception ex) { Log("Error: " + t.Title + ": " + ex.Message); } }
                Log(Tf("{0} ajustes restaurados a los valores de Windows", todo.Count));
                Ui(() => Toast(Tr("Windows restaurado a sus valores por defecto"), 0));
                RefreshTweakStates();
            });
        }

        // ============================================================ clean
        void BuildClean()
        {
            var list = E<StackPanel>("CleanList");
            var sel = settings.CleanSel ?? Cleaner.Items.Where(i => i.Def).Select(i => i.Id).ToList();
            bool first = true;
            foreach (var it in Cleaner.Items)
            {
                var sw = new CheckBox { Style = St("Switch"), IsChecked = sel.Contains(it.Id) };
                sw.Click += (s, e) => { settings.CleanSel = Cleaner.Items.Where(x => cleanSwitches[x.Id].IsChecked == true).Select(x => x.Id).ToList(); UpdateCleanTotal(); };
                cleanSwitches[it.Id] = sw;
                var size = new TextBlock { Text = it.Kind == 2 ? "" : "—", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Br("Sub"), MinWidth = 70, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
                cleanSizes[it.Id] = size;
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                right.Children.Add(size); right.Children.Add(sw);
                list.Children.Add(Row(it.Name, it.Desc, null, right, first));
                first = false;
            }
            E<Button>("BtnAnalyze").Click += (s, e) => Analyze();
            E<Button>("BtnCleanNow").Click += (s, e) => CleanSelected();
            UpdateCleanLast();
        }

        void UpdateCleanLast()
        {
            E<TextBlock>("CleanLast").Text = settings.LastClean > 0 ? Tr("Última limpieza") + " " + Ago(FromSec(settings.LastClean)) + "\n" + Tf("{0} liberados", Sys.Fmt(settings.LastCleanBytes)) : "";
        }

        bool analyzing;
        void Analyze()
        {
            if (analyzing) return; analyzing = true;
            E<TextBlock>("CleanTotalSub").Text = Tr("Analizando...");
            foreach (var it in Cleaner.Items) if (it.Kind != 2) cleanSizes[it.Id].Text = "...";
            Bg(() =>
            {
                foreach (var it in Cleaner.Items)
                {
                    it.Size = Cleaner.Measure(it);
                    Ui(() => UpdateCleanSizes());
                }
                Ui(() =>
                {
                    analyzing = false;
                    junkBytes = new[] { "temp", "wintemp", "prefetch", "dumps", "recycle", "delivery" }.Sum(id => Math.Max(0, Cleaner.Get(id).Size));
                    UpdateCleanTotal(); BuildRecs();
                });
            });
        }

        void UpdateCleanSizes()
        {
            foreach (var it in Cleaner.Items)
            {
                if (it.Kind == 2) continue;
                var tb = cleanSizes[it.Id];
                tb.Text = it.Size < 0 ? "—" : Sys.Fmt(it.Size);
                tb.Foreground = it.Size > 100L * 1024 * 1024 ? Br("Text") : Br("Sub");
            }
            UpdateCleanTotal();
        }

        void UpdateCleanTotal()
        {
            if (Cleaner.Items.All(i => i.Size < 0)) return;
            long tot = Cleaner.Items.Where(i => cleanSwitches[i.Id].IsChecked == true && i.Size > 0).Sum(i => i.Size);
            E<TextBlock>("CleanTotal").Text = Sys.Fmt(tot);
            E<TextBlock>("CleanTotalSub").Text = analyzing ? Tr("Analizando...") : Tr("pueden liberarse con la selección actual");
        }

        void CleanSelected()
        {
            var sel = Cleaner.Items.Where(i => cleanSwitches[i.Id].IsChecked == true).ToList();
            if (sel.Count == 0) { Toast(Tr("Selecciona al menos una categoría"), 3); return; }
            RunClean(sel);
        }

        void QuickClean()
        {
            var ids = new List<string> { "temp", "wintemp", "dumps", "recycle", "dns", "delivery" };
            if (sys == null || !sys.IsHdd) ids.Add("prefetch");
            RunClean(ids.Select(Cleaner.Get).ToList());
        }

        void RunClean(List<CleanItem> items)
        {
            Toast(Tf("Limpiando {0} categorías...", items.Count), 2, true);
            E<Button>("BtnCleanNow").IsEnabled = false;
            Bg(() =>
            {
                long total = 0;
                foreach (var it in items)
                {
                    long f = Cleaner.Clean(it);
                    total += f;
                    if (it.Kind != 2) Log(Tr("Limpieza") + " · " + it.Name + ": " + Sys.Fmt(f));
                    it.Size = Cleaner.Measure(it);
                }
                Ui(() =>
                {
                    E<Button>("BtnCleanNow").IsEnabled = true;
                    settings.LastClean = NowSec(); settings.LastCleanBytes = total;
                    Store.Save("settings.json", settings);
                    junkBytes = new[] { "temp", "wintemp", "prefetch", "dumps", "recycle", "delivery" }.Sum(id => Math.Max(0, Cleaner.Get(id).Size));
                    UpdateCleanSizes(); UpdateCleanLast(); BuildRecs();
                    Toast(Tf("Limpieza completa · {0} liberados", Sys.Fmt(total)), 0);
                    Log(Tf("Limpieza completa · {0} liberados", Sys.Fmt(total)));
                });
            });
        }

        // ============================================================ benchmark
        static readonly BenchMetric[] Metrics =
        {
            new BenchMetric { Glyph = "", Title = "CPU · un núcleo", Get = r => r.Single, Fmt = "N0", Unit = "", Higher = true },
            new BenchMetric { Glyph = "", Title = "CPU · multinúcleo", Get = r => r.Multi, Fmt = "N0", Unit = "", Higher = true },
            new BenchMetric { Glyph = "", Title = "Temporizador", Get = r => r.TimerMs, Fmt = "0.00", Unit = " ms", Higher = false },
            new BenchMetric { Glyph = "", Title = "CPU en segundo plano", Get = r => r.IdleCpu, Fmt = "0.0", Unit = "%", Higher = false },
            new BenchMetric { Glyph = "", Title = "Procesos activos", Get = r => r.Procs, Fmt = "N0", Unit = "", Higher = false },
            new BenchMetric { Glyph = "", Title = "RAM libre", Get = r => r.RamFreeGB, Fmt = "0.0", Unit = " GB", Higher = true },
            new BenchMetric { Glyph = "", Title = "Ping", Get = r => r.Ping, Fmt = "0", Unit = " ms", Higher = false },
            new BenchMetric { Glyph = "", Title = "Jitter", Get = r => r.Jitter, Fmt = "0.0", Unit = " ms", Higher = false },
        };

        BenchResult Baseline
        {
            get
            {
                if (bench.Items.Count == 0) return null;
                int i = settings.BaselineIdx;
                return i >= 0 && i < bench.Items.Count ? bench.Items[i] : bench.Items[0];
            }
        }

        void WireBench()
        {
            E<Button>("BtnBench").Click += (s, e) => RunBench();
            E<Button>("BtnBaseline").Click += (s, e) => SetBaseline(bench.Items.Count - 1);
        }

        void SetBaseline(int idx)
        {
            settings.BaselineIdx = idx;
            Store.Save("settings.json", settings);
            BuildBench();
            Toast(Tr("Referencia actualizada"), 0);
        }

        // Devuelve (texto, fg, bg) del indicador de cambio
        static string[] DeltaStyle(double now, double baseV, bool higherBetter)
        {
            if (baseV <= 0 || now < 0) return null;
            double pct = (now - baseV) / baseV * 100;
            string txt = (pct >= 0 ? "+" : "−") + Math.Abs(pct).ToString("0.0") + "%";
            if (Math.Abs(pct) < 1.5) return new[] { "≈ " + Math.Abs(pct).ToString("0.0") + "%", "#8A90A2", "#1A1E29" };
            bool better = higherBetter ? pct > 0 : pct < 0;
            return better ? new[] { txt, "#4ADE80", "#11261A" } : new[] { txt, "#F87171", "#2A1414" };
        }

        void BuildBench()
        {
            var last = bench.Items.LastOrDefault();
            var bl = Baseline;
            E<Button>("BtnBaseline").Visibility = last != null && last != bl ? Visibility.Visible : Visibility.Collapsed;
            var grid = E<UniformGrid>("BenchGrid");
            var hist = E<StackPanel>("BenchHistory");
            grid.Children.Clear(); hist.Children.Clear();
            var deltaB = E<Border>("BenchDelta");
            if (last == null)
            {
                E<TextBlock>("BenchScore").Text = "—";
                deltaB.Visibility = Visibility.Collapsed;
                E<TextBlock>("BenchSub").Text = Tr("Aún no hay resultados. Ejecuta el primer benchmark (tarda unos 15 segundos).");
                var c = Cap(Tr("El historial aparecerá aquí.")); c.Margin = new Thickness(20, 14, 20, 14);
                hist.Children.Add(c);
                return;
            }
            E<TextBlock>("BenchScore").Text = last.Score.ToString("N0");
            var ds = bl != null && bl != last ? DeltaStyle(last.Score, bl.Score, true) : null;
            if (ds != null)
            {
                deltaB.Visibility = Visibility.Visible; deltaB.Background = Hex(ds[2]);
                var dt = E<TextBlock>("BenchDeltaText"); dt.Text = ds[0]; dt.Foreground = Hex(ds[1]);
                E<TextBlock>("BenchSub").Text = Tf("Comparado con tu referencia del {0}", DateStr(bl.Time)) + (last.GameMode ? " · " + Tr("medido con Modo Juego activo") : "");
            }
            else
            {
                deltaB.Visibility = Visibility.Collapsed;
                E<TextBlock>("BenchSub").Text = Tr("Este resultado es tu referencia. Optimiza tu PC o activa el Modo Juego y vuelve a medir para ver la mejora.");
            }

            foreach (var m in Metrics)
            {
                double v = m.Get(last);
                var card = new Border { Style = St("CardB"), Margin = new Thickness(6), Padding = new Thickness(16, 14, 16, 14) };
                var sp = new StackPanel();
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(Glyph(m.Glyph, 12, Br("Mute")));
                var tt = Txt(Tr(m.Title), 12, Br("Sub")); tt.Margin = new Thickness(8, 0, 0, 0);
                head.Children.Add(tt);
                sp.Children.Add(head);
                var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                var val = Txt(v < 0 ? "—" : v.ToString(m.Fmt) + m.Unit, 20, Br("Text"), FontWeights.SemiBold);
                row.Children.Add(val);
                var d = bl != null && bl != last ? DeltaStyle(v, m.Get(bl), m.Higher) : null;
                if (d != null) { var b = Badge(d[0], d[1], d[2]); b.Margin = new Thickness(8, 4, 0, 0); row.Children.Add(b); }
                sp.Children.Add(row);
                if (bl != null && bl != last)
                {
                    double bv = m.Get(bl);
                    var c = Cap(Tr("Referencia") + ": " + (bv < 0 ? "—" : bv.ToString(m.Fmt) + m.Unit)); c.FontSize = 11.5; c.Margin = new Thickness(0, 4, 0, 0);
                    sp.Children.Add(c);
                }
                card.Child = sp;
                grid.Children.Add(card);
            }

            bool first = true;
            for (int i = bench.Items.Count - 1; i >= 0 && i >= bench.Items.Count - 10; i--)
            {
                var r = bench.Items[i];
                int idx = i;
                var badges = new List<UIElement>();
                if (r == bl) badges.Add(Badge(Tr("Referencia"), "#C9C2FF", "#2B2260"));
                if (r.GameMode) badges.Add(Badge(Tr("Modo Juego"), "#4ADE80", "#11261A"));
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                var sc = Txt(r.Score.ToString("N0"), 15, Br("Text"), FontWeights.SemiBold); sc.VerticalAlignment = VerticalAlignment.Center; sc.Margin = new Thickness(0, 0, 14, 0);
                right.Children.Add(sc);
                if (r != bl)
                {
                    var b = new Button { Style = St("Ghost"), Content = new TextBlock { Text = Tr("Usar como referencia"), FontSize = 12 }, Padding = new Thickness(10, 5, 10, 5) };
                    b.Click += (s, e) => SetBaseline(idx);
                    right.Children.Add(b);
                }
                hist.Children.Add(Row(DateStr(r.Time), Tf("CPU {0} / {1} · Temporizador {2} ms · Ping {3} ms", r.Single.ToString("N0"), r.Multi.ToString("N0"), r.TimerMs.ToString("0.00"), r.Ping < 0 ? "—" : r.Ping.ToString("0")), badges, right, first));
                first = false;
            }
        }

        void RunBench()
        {
            if (benchRunning) return;
            benchRunning = true;
            E<Button>("BtnBench").IsEnabled = false;
            E<StackPanel>("BenchProgress").Visibility = Visibility.Visible;
            E<Border>("BenchBar").Width = 0;
            bool gm = sessionActive;
            Bg(() =>
            {
                BenchResult r = null;
                try
                {
                    r = Bench.Run((p, step) => Ui(() =>
                    {
                        if (step != "") E<TextBlock>("BenchStep").Text = step;
                        E<Border>("BenchBar").BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(260 * p / 100.0, TimeSpan.FromMilliseconds(400)));
                    }), gm);
                }
                finally
                {
                    var res = r;
                    Ui(() =>
                    {
                        benchRunning = false;
                        E<Button>("BtnBench").IsEnabled = true;
                        E<StackPanel>("BenchProgress").Visibility = Visibility.Collapsed;
                        if (res == null) return;
                        bench.Items.Add(res);
                        if (bench.Items.Count > 50)
                        {
                            bench.Items.RemoveAt(0);
                            if (settings.BaselineIdx > 0) settings.BaselineIdx--; else settings.BaselineIdx = -1;
                        }
                        Store.Save("bench.json", bench);
                        BuildBench(); BuildRecs();
                        Toast(Tf("Benchmark completado · {0} puntos", res.Score.ToString("N0")), 0);
                        Log(Tf("Benchmark completado · {0} puntos", res.Score.ToString("N0")));
                    });
                }
            });
        }

        // ============================================================ settings
        void BuildGeneral()
        {
            var list = E<StackPanel>("GeneralList");
            list.Children.Clear();

            // Idioma
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            var restart = Btn(Tr("Reiniciar ahora"), "Secondary", () => { RelaunchDelayed(Installer.CurrentExe, ""); ExitApp(); });
            restart.Visibility = Visibility.Collapsed; restart.Margin = new Thickness(0, 0, 10, 0);
            foreach (var opt in new[] { new[] { "auto", Tr("Automático") }, new[] { "es", "Español" }, new[] { "en", "English" } })
            {
                string code = opt[0];
                var rb = new RadioButton { Style = St("Seg"), Content = opt[1], GroupName = "lang", IsChecked = (settings.Lang ?? "auto") == code };
                rb.Checked += (s, e) => { settings.Lang = code; Store.Save("settings.json", settings); restart.Visibility = Visibility.Visible; };
                seg.Children.Add(rb);
            }
            var segBorder = new Border { Background = Hex("#0E1117"), CornerRadius = new CornerRadius(9), Padding = new Thickness(3), BorderBrush = Br("Line"), BorderThickness = new Thickness(1), Child = seg };
            var langRight = new StackPanel { Orientation = Orientation.Horizontal };
            langRight.Children.Add(restart); langRight.Children.Add(segBorder);
            list.Children.Add(Row(Tr("Idioma") + " · Language", Tr("Se aplica al reiniciar GameBoost."), null, langRight, true));

            Action<string, string, Func<bool>, Action<bool>> toggle = (title, desc, get, set) =>
            {
                var sw = new CheckBox { Style = St("Switch"), IsChecked = get() };
                sw.Click += (s, e) => { set(sw.IsChecked == true); Store.Save("settings.json", settings); };
                list.Children.Add(Row(title, desc, null, sw, false));
            };
            toggle(Tr("Detectar juegos automáticamente"), Tr("Activa el Modo Juego solo cuando abres un juego, aunque lo inicies desde Steam, Epic o un acceso directo."), () => settings.AutoDetect, v => settings.AutoDetect = v);
            toggle(Tr("Seguir en la bandeja al cerrar"), Tr("Al cerrar la ventana, GameBoost sigue en la bandeja del sistema para detectar tus juegos."), () => settings.TrayOnClose, v => settings.TrayOnClose = v);

            var auto = new CheckBox { Style = St("Switch"), IsEnabled = false };
            auto.Click += (s, e) =>
            {
                bool on = auto.IsChecked == true;
                auto.IsEnabled = false;
                Bg(() =>
                {
                    Installer.SetAutostart(on);
                    bool now = Installer.AutostartEnabled();
                    Ui(() =>
                    {
                        auto.IsChecked = now; auto.IsEnabled = true;
                        Toast(now ? Tr("GameBoost se iniciará con Windows (en la bandeja)") : Tr("GameBoost ya no se iniciará con Windows"), now == on ? 0 : 3);
                    });
                });
            };
            Bg(() => { bool on = Installer.AutostartEnabled(); Ui(() => { auto.IsChecked = on; auto.IsEnabled = true; }); });
            list.Children.Add(Row(Tr("Iniciar con Windows"), Tr("Arranca minimizado en la bandeja al iniciar sesión, listo para detectar tus juegos."), null, auto, false));

            toggle(Tr("Buscar actualizaciones automáticamente"), Tr("Comprueba al iniciar si hay una versión nueva de GameBoost."), () => settings.CheckUpdates, v => settings.CheckUpdates = v);

            // Instalación
            bool installed = Installer.RunningInstalled;
            FrameworkElement instBtn = installed
                ? (FrameworkElement)Btn(Tr("Desinstalar"), "Secondary", () =>
                {
                    try { Process.Start(new ProcessStartInfo(Installer.ExePath, "--uninstall") { UseShellExecute = true }); ExitApp(); }
                    catch (Exception ex) { Toast(ex.Message, 3); }
                })
                : Btn(Installer.IsInstalled ? Tr("Actualizar instalación") : Tr("Instalar en este PC"), "Primary", InstallSelf);
            list.Children.Add(Row(installed ? Tr("Instalado en este PC") : Tr("Versión portátil"),
                installed ? Installer.InstallDir : Tr("Instálalo para tener acceso en el menú Inicio, inicio con Windows y desinstalador en Configuración > Aplicaciones."),
                null, instBtn, false));
        }

        void InstallSelf()
        {
            Toast(Tr("Instalando GameBoost..."), 2, true);
            Bg(() =>
            {
                string src = Installer.CurrentExe;
                Installer.Install(() => File.OpenRead(src), true, false, step => Toast(step, 2, true));
                Ui(() =>
                {
                    Toast(Tr("Instalado. Abriendo la versión instalada..."), 0);
                    Log(Tr("GameBoost instalado en") + " " + Installer.InstallDir);
                    RelaunchDelayed(Installer.ExePath, "");
                    ExitApp();
                });
            });
        }

        void UpdateBackupInfo()
        {
            var d = Backup.Data;
            int n = Backup.Count;
            var btn = E<Button>("BtnRestoreBackup");
            if (n == 0)
            {
                E<TextBlock>("BackupInfo").Text = Tr("Todavía no hay copia: se crea automáticamente la primera vez que GameBoost cambia un ajuste.");
                btn.IsEnabled = false;
            }
            else
            {
                E<TextBlock>("BackupInfo").Text = Tf("Copia creada el {0} · {1} valores originales guardados. Devuelve Windows exactamente a como estaba antes de GameBoost.", DateStr(d.Created), n);
                btn.IsEnabled = true;
            }
        }

        void BuildSettings()
        {
            var list = E<StackPanel>("SessionList");
            var defs = new List<Tuple<string, string, Func<bool>, Action<bool>>>
            {
                Tuple.Create(Tr("Aplicar optimizaciones recomendadas"), Tr("Activa automáticamente los ajustes recomendados para tu PC que falten."), (Func<bool>)(() => settings.AutoTweaks), (Action<bool>)(v => settings.AutoTweaks = v)),
                Tuple.Create(Tr("Limpiar temporales"), Tr("Borra temporales y volcados de errores antes de abrir el juego."), (Func<bool>)(() => settings.CleanTemp), (Action<bool>)(v => settings.CleanTemp = v)),
                Tuple.Create(Tr("Cerrar apps en segundo plano"), Tr("Cierra las apps de la lista de abajo (OneDrive, Teams, Widgets...)."), (Func<bool>)(() => settings.CloseApps), (Action<bool>)(v => settings.CloseApps = v)),
                Tuple.Create(Tr("Reabrir apps al terminar"), Tr("Vuelve a abrir las apps cerradas cuando sales del juego."), (Func<bool>)(() => settings.ReopenApps), (Action<bool>)(v => settings.ReopenApps = v)),
                Tuple.Create(Tr("Pausar servicios de Windows"), Tr("Detiene indexación, telemetría y Windows Update durante la partida."), (Func<bool>)(() => settings.PauseServices), (Action<bool>)(v => settings.PauseServices = v)),
                Tuple.Create(Tr("Liberar memoria RAM en espera"), Tr("Vacía la lista standby para que el juego tenga más RAM libre al cargar."), (Func<bool>)(() => settings.PurgeRam), (Action<bool>)(v => settings.PurgeRam = v)),
                Tuple.Create(Tr("Limpiar caché DNS"), Tr("Evita fallos de conexión a servidores con direcciones antiguas."), (Func<bool>)(() => settings.FlushDns), (Action<bool>)(v => settings.FlushDns = v)),
                Tuple.Create(Tr("Timer del sistema a 0.5 ms"), Tr("Mejora la precisión del frametime y del input mientras dure la sesión."), (Func<bool>)(() => settings.Timer), (Action<bool>)(v => settings.Timer = v)),
                Tuple.Create(Tr("Restaurar todo al cerrar el juego"), Tr("Reanuda servicios y apps automáticamente cuando el juego se cierra."), (Func<bool>)(() => settings.RestoreOnExit), (Action<bool>)(v => settings.RestoreOnExit = v)),
                Tuple.Create(Tr("Minimizar GameBoost al jugar"), Tr("Se oculta en la barra de tareas al lanzar el juego."), (Func<bool>)(() => settings.Minimize), (Action<bool>)(v => settings.Minimize = v)),
            };
            bool first = true;
            foreach (var d in defs)
            {
                var def = d;
                var sw = new CheckBox { Style = St("Switch"), IsChecked = def.Item3() };
                sw.Click += (s, e) => { def.Item4(sw.IsChecked == true); Store.Save("settings.json", settings); };
                list.Children.Add(Row(def.Item1, def.Item2, null, sw, first));
                first = false;
            }
            var kb = E<TextBox>("KillBox"); kb.Text = settings.KillList;
            kb.LostFocus += (s, e) => { settings.KillList = kb.Text; Store.Save("settings.json", settings); };
            var sb = E<TextBox>("SvcBox"); sb.Text = settings.Services;
            sb.LostFocus += (s, e) => { settings.Services = sb.Text; Store.Save("settings.json", settings); };
            E<Button>("BtnOpenData").Click += (s, e) => { Directory.CreateDirectory(Store.Dir); Open(Store.Dir); };
            E<Button>("BtnCheckUpdates").Click += (s, e) => CheckUpdates(true);
            E<Button>("BtnRestoreBackup").Click += (s, e) =>
            {
                var b = E<Button>("BtnRestoreBackup");
                if (!restoreArmed) { restoreArmed = true; b.Content = Tr("¿Seguro? Pulsa otra vez"); return; }
                restoreArmed = false; b.Content = Tr("Restaurar mi configuración original"); b.IsEnabled = false;
                Toast(Tr("Restaurando tu configuración original..."), 2, true);
                Bg(() =>
                {
                    int n = Backup.Restore();
                    rebootPending = true;
                    Log(Tf("Configuración original restaurada · {0} valores", n));
                    Ui(() => { Toast(Tf("Configuración original restaurada · {0} valores", n) + " · " + Tr("reinicia para completar"), 0); b.IsEnabled = true; });
                    RefreshTweakStates();
                });
            };
            E<Button>("BtnRestorePoint").Click += (s, e) =>
            {
                Toast(Tr("Creando punto de restauración (puede tardar un minuto)..."), 2, true);
                Bg(() =>
                {
                    string o = Sys.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction SilentlyContinue; Checkpoint-Computer -Description 'GameBoost' -RestorePointType MODIFY_SETTINGS\"");
                    bool ok = string.IsNullOrWhiteSpace(o);
                    Log(ok ? Tr("Punto de restauración creado") : Tr("Punto de restauración") + ": " + o.Trim());
                    Toast(ok ? Tr("Punto de restauración creado") : Tr("Windows solo permite 1 punto cada 24 h o la protección está desactivada"), ok ? 0 : 3);
                });
            };
        }

        // ============================================================ tips
        static bool Rx(string s, string pattern) { return System.Text.RegularExpressions.Regex.IsMatch(s ?? "", pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }

        void BuildTips()
        {
            var list = E<StackPanel>("TipsList");
            list.Children.Clear();
            var mine = new List<string[]>();
            if (sys != null)
            {
                if (sys.GpuVendor == "nvidia")
                {
                    mine.Add(new[] { "", Tr("NVIDIA Reflex: Activado + Boost"), Tr("En Fortnite, Valorant, CS2, Apex y COD. Es la mayor reducción de latencia que existe: hasta un 30-50% menos.") });
                    mine.Add(new[] { "", Tr("Panel de control NVIDIA"), Tr("Administración de energía: 'Preferir rendimiento máximo'. Modo de baja latencia: 'Activado' en juegos sin Reflex. Tamaño de caché de shaders: 10 GB.") });
                    if (Rx(sys.Gpu, @"RTX\s*[2-5]0")) mine.Add(new[] { "", Tf("DLSS en tu {0}", sys.Gpu.Replace("NVIDIA ", "").Replace("GeForce ", "")), Tr("Activa DLSS en modo Calidad o Equilibrado: más FPS con una imagen casi idéntica a la nativa.") + (Rx(sys.Gpu, @"RTX\s*[45]0") ? " " + Tr("Tu GPU también soporta Frame Generation.") : "") });
                }
                else if (sys.GpuVendor == "amd" && sys.DedicatedGpu)
                {
                    mine.Add(new[] { "", "AMD Anti-Lag 2", Tr("Actívalo en Adrenalin (y en el juego si lo soporta). Desactiva Radeon Chill y Enhanced Sync para mínima latencia.") });
                    mine.Add(new[] { "", Tr("Adrenalin: perfil eSports"), Tr("Usa el perfil gráfico 'eSports' y FSR en los juegos que lo incluyan para ganar FPS.") });
                }
                else if (!sys.DedicatedGpu)
                    mine.Add(new[] { "", Tr("Gráficos integrados"), Tr("Usa FSR/XeSS en modo Rendimiento, resolución 1080p o menor y, si tu BIOS lo permite, asigna más memoria de vídeo (UMA Frame Buffer).") });
                if (sys.DualX3D)
                    mine.Add(new[] { "", Tf("Tu {0}", sys.Cpu), Tr("Mantén el plan Equilibrado, la Game Bar y los drivers de chipset de AMD al día: así los juegos usan el CCD con 3D V-Cache.") });
                if (sys.MaxHz > 60)
                    mine.Add(new[] { "", Tf("Pantalla de {0} Hz", sys.MaxHz), (sys.Hz < sys.MaxHz ? Tf("Ahora está a {0} Hz: súbela a {1} Hz en Configuración > Pantalla > Pantalla avanzada.", sys.Hz, sys.MaxHz) + " " : "") +
                        Tf("Con G-Sync/FreeSync limita los FPS a {0}; sin sincronización, déjalos sin límite.", sys.MaxHz - 3) + (sys.HasBattery ? "" : " " + Tr("Usa cable DisplayPort o HDMI 2.0+.")) });
                if (sys.RamGB > 0 && sys.RamGB < 16)
                    mine.Add(new[] { "", Tf("{0} GB de RAM", sys.RamGB), Tr("Cierra el navegador antes de jugar: Chrome puede ocupar 1-3 GB. El Modo Juego ya cierra las apps en segundo plano.") });
                if (sys.HasBattery)
                    mine.Add(new[] { "", Tr("Portátil"), Tr("Juega enchufado y sobre una superficie dura o una base con ventilación: el calor es lo que más rendimiento le quita a un portátil.") + (sys.HybridGraphics ? " " + Tr("Si tu marca tiene modo 'GPU dedicada / MUX', actívalo para unos FPS extra.") : "") });
            }
            if (mine.Count > 0) AddTipSection(list, Tr("PARA TU EQUIPO"), mine);

            AddTipSection(list, Tr("FORTNITE Y SHOOTERS COMPETITIVOS"), new List<string[]>
            {
                new[] { "", Tr("Modo de renderizado"), Tr("Fortnite: 'Rendimiento' da el máximo de FPS en PCs medias. DirectX 12 es mejor con GPU moderna, pero la primera partida compila shaders.") },
                new[] { "", Tr("Limita los FPS"), Tr("Un tope estable (Hz del monitor o un poco más) da un frametime más parejo que FPS altos que suben y bajan.") },
                new[] { "", Tr("Pantalla completa"), Tr("Usa pantalla completa o ventana sin bordes con 'Optimizaciones para juegos en ventana' activado (pestaña Optimizar).") },
                new[] { "", Tr("Calidad baja donde no se nota"), Tr("Sombras, efectos, post-procesado y hierba en bajo: más FPS y mejor visibilidad. Texturas según la VRAM de tu GPU.") },
                new[] { "", Tr("Mouse a 1000 Hz o más"), Tr("Configura el polling rate en el software de tu mouse y usa la sensibilidad de Windows en 6/11 (por defecto).") },
                new[] { "", Tr("Verifica los archivos del juego"), Tr("Si tienes tirones o crasheos: Epic > Biblioteca > ... > Administrar > Verificar. En Steam: Propiedades > Archivos instalados.") },
            });

            AddTipSection(list, Tr("WINDOWS Y HARDWARE"), new List<string[]>
            {
                new[] { "", Tr("Activa XMP / EXPO en la BIOS"), Tr("La RAM viene por defecto a velocidad básica. Activar su perfil puede dar un 5-15% más de FPS en juegos que dependen de la CPU.") },
                new[] { "", Tr("Drivers de GPU al día"), Tr("Actualiza desde NVIDIA App / AMD Adrenalin. Si hay problemas, haz una instalación limpia con DDU en modo seguro.") },
                new[] { "", Tr("Vigila las temperaturas"), Tr("CPU por encima de 90 °C o GPU por encima de 85 °C provocan throttling. Limpia el polvo y revisa la pasta térmica cada 2-3 años.") },
                new[] { "", Tr("Juegos en SSD / NVMe"), Tr("Cargas mucho más rápidas y menos tirones al cargar zonas. Deja al menos un 15% del disco libre.") },
                new[] { "", Tr("Reinicia de verdad"), Tr("Apagar con 'Inicio rápido' no limpia la memoria del kernel. Reinicia la PC cada pocos días o antes de una sesión larga.") },
                new[] { "", Tr("Evita los 'limpiadores de registro'"), Tr("No mejoran el rendimiento y pueden romper Windows. Un solo antivirus (Defender es suficiente).") },
            });

            AddTipSection(list, Tr("RED"), new List<string[]>
            {
                new[] { "", Tr("Usa cable Ethernet"), Tr("El Wi-Fi añade jitter y pérdida de paquetes. Si no puedes, usa 5 GHz y ponte cerca del router.") },
                new[] { "", Tr("Elige bien la región"), Tr("Juega en el servidor con menor ping (en Fortnite: Configuración > Juego > Región de matchmaking).") },
                new[] { "", Tr("Cuidado con las descargas"), Tr("Steam, Epic, OneDrive y Windows Update descargando en segundo plano disparan el ping. El Modo Juego pausa Windows Update.") },
                new[] { "", Tr("QoS en el router"), Tr("Si compartes conexión, activa QoS / prioridad para gaming en el router para la IP de tu PC.") },
            });
        }

        UniformGrid CardGrid(StackPanel list, string title)
        {
            list.Children.Add(new TextBlock { Text = title, Style = St("Sec") });
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 0, -6, 0) };
            list.Children.Add(grid);
            return grid;
        }

        void AddTipSection(StackPanel list, string title, List<string[]> tips)
        {
            var grid = CardGrid(list, title);
            foreach (var t in tips)
            {
                var card = new Border { Style = St("CardB"), Margin = new Thickness(6), Padding = new Thickness(18, 16, 18, 16) };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(IconBox(t[0], "#A996FF", "#1C1838"));
                var sp = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                var h = Txt(t[1], 13.5, Br("Text"), FontWeights.SemiBold); h.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(h);
                var c = Cap(t[2]); c.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(c);
                Grid.SetColumn(sp, 1); g.Children.Add(sp);
                card.Child = g;
                grid.Children.Add(card);
            }
        }

        // ============================================================ recommended apps
        void BuildApps()
        {
            var list = E<StackPanel>("AppsList");
            list.Children.Clear();
            bool known = sys != null;
            bool nv = known && sys.GpuVendor == "nvidia", amdG = known && sys.GpuVendor == "amd" && sys.DedicatedGpu, amdC = known && sys.CpuVendor == "amd";
            bool intel = known && (sys.CpuVendor == "intel" || sys.GpuVendor == "intel");
            bool weakGpu = known && (!sys.DedicatedGpu || sys.HasBattery);
            string free = Tr("Gratis");

            var drivers = new List<AppRec>();
            if (nv || !known) drivers.Add(new AppRec { Name = "NVIDIA App", Glyph = "", Price = free, Mine = nv, Url = UrlNvidia, Key = "NVIDIA app", Desc = Tr("Drivers Game Ready, ajustes óptimos por juego, filtros, ShadowPlay para grabar y overlay de rendimiento.") });
            if (amdG || !known) drivers.Add(new AppRec { Name = "AMD Software: Adrenalin", Glyph = "", Price = free, Mine = amdG, Url = UrlAmdGpu, Key = "AMD Software", Desc = Tr("Drivers Radeon con Anti-Lag 2, HYPR-RX, Radeon Boost, FSR a nivel de driver y grabación ReLive.") });
            if (amdC || !known) drivers.Add(new AppRec { Name = Tr("Drivers de chipset AMD"), Glyph = "", Price = free, Mine = amdC, Url = UrlAmdDrivers, Key = "AMD Chipset Software", Desc = Tr("Mejoran el planificador de núcleos y la energía de los Ryzen. Imprescindibles en los X3D.") });
            if (intel || !known) drivers.Add(new AppRec { Name = "Intel Driver & Support Assistant", Glyph = "", Price = free, Mine = intel, Url = UrlIntel, Key = "Driver & Support Assistant", Desc = Tr("Detecta y actualiza drivers de chipset, gráficos, Wi-Fi y Bluetooth de Intel.") });
            AddAppSection(list, Tr("DRIVERS PARA TU EQUIPO"), drivers);

            AddAppSection(list, Tr("MONITOREO"), new List<AppRec>
            {
                new AppRec { Name = "MSI Afterburner + RivaTuner", Glyph = "", Price = free, Url = "https://www.msi.com/Landing/afterburner/graphics-cards", Key = "MSI Afterburner", Desc = Tr("Overlay con FPS, frametime, temperaturas y uso en el juego. RivaTuner es el limitador de FPS más estable que existe.") },
                new AppRec { Name = "HWiNFO", Glyph = "", Price = free, Mine = known && sys.HasBattery, Url = "https://www.hwinfo.com/download/", Key = "HWiNFO", Desc = Tr("Todos los sensores del PC: temperaturas, consumo y si la CPU o GPU están haciendo throttling.") },
                new AppRec { Name = "CPU-Z", Glyph = "", Price = free, Url = "https://www.cpuid.com/softwares/cpu-z.html", Key = "CPU-Z", Desc = Tr("Comprueba la velocidad real de tu RAM (si XMP/EXPO está activo) y los datos exactos de tu CPU.") },
                new AppRec { Name = "CrystalDiskInfo", Glyph = "", Price = free, Mine = known && sys.IsHdd, Url = "https://crystalmark.info/en/software/crystaldiskinfo/", Key = "CrystalDiskInfo", Desc = Tr("Salud y temperatura de tus SSD y discos. Te avisa antes de que un disco falle.") },
            });

            AddAppSection(list, Tr("RENDIMIENTO Y MANTENIMIENTO"), new List<AppRec>
            {
                new AppRec { Name = "Process Lasso", Glyph = "", Price = Tr("Gratis / Pro"), Mine = known && (sys.IntelHybrid || sys.DualX3D), Url = "https://bitsum.com/", Key = "Process Lasso", Desc = Tr("Mantiene la prioridad y afinidad de cada juego de forma automática y evita que otros procesos le roben CPU.") },
                new AppRec { Name = "Display Driver Uninstaller (DDU)", Glyph = "", Price = free, Url = "https://www.guru3d.com/download/display-driver-uninstaller-download/", Key = null, Desc = Tr("Borra por completo el driver de la GPU antes de reinstalarlo. La solución a crasheos y tirones tras actualizar.") },
                new AppRec { Name = "Bulk Crap Uninstaller", Glyph = "", Price = free, Url = "https://www.bcuninstaller.com/", Key = "BCUninstaller", Desc = Tr("Desinstala bloatware y programas en lote, y limpia los restos que dejan.") },
                new AppRec { Name = "Lossless Scaling", Glyph = "", Price = Tr("De pago"), Mine = weakGpu, Url = "https://store.steampowered.com/app/993090/Lossless_Scaling/", Key = null, Desc = Tr("Frame generation y escalado para cualquier juego. Muy útil en portátiles y GPUs modestas para duplicar FPS.") },
            });

            AddAppSection(list, Tr("GRABAR Y TRANSMITIR"), new List<AppRec>
            {
                new AppRec { Name = "OBS Studio", Glyph = "", Price = free, Url = "https://obsproject.com/", Key = "OBS Studio", Desc = Tf("Graba y transmite usando el encoder de tu GPU ({0}) casi sin perder FPS. Reemplaza al Game DVR que desactivamos.", nv ? "NVENC" : amdG ? "AMF" : "hardware") },
            });
        }

        void AddAppSection(StackPanel list, string title, List<AppRec> apps)
        {
            if (apps.Count == 0) return;
            var grid = CardGrid(list, title);
            foreach (var a in apps.OrderByDescending(x => x.Mine))
            {
                var app = a;
                bool inst = IsInstalled(app.Key);
                var card = new Border { Style = St("CardB"), Margin = new Thickness(6), Padding = new Thickness(18, 16, 18, 16) };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(IconBox(app.Glyph, app.Mine ? "#FFFFFF" : "#A996FF", app.Mine ? "#5B47D6" : "#1C1838", 38));
                var sp = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                sp.Children.Add(Txt(app.Name, 13.5, Br("Text"), FontWeights.SemiBold));
                var badges = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
                if (app.Mine) badges.Children.Add(Badge(Tr("Para tu PC"), "#C9C2FF", "#2B2260"));
                if (inst) badges.Children.Add(Badge(Tr("Instalado"), "#4ADE80", "#11261A"));
                badges.Children.Add(Badge(app.Price, "#8A90A2", "#1A1E29"));
                sp.Children.Add(badges);
                var c = Cap(app.Desc); c.Margin = new Thickness(0, 8, 0, 12);
                sp.Children.Add(c);
                var b = Btn(inst ? Tr("Sitio web") : Tr("Descargar"), inst ? "Secondary" : "Primary", () => Open(app.Url));
                b.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(b);
                Grid.SetColumn(sp, 1); g.Children.Add(sp);
                card.Child = g;
                grid.Children.Add(card);
            }
        }

        // ============================================================ session
        static string GameName(string proc)
        {
            string n;
            return KnownGames.TryGetValue(proc, out n) ? n : proc;
        }

        // Vigila los procesos y activa el Modo Juego automáticamente al abrir un juego
        void WatchLoop()
        {
            while (!closing)
            {
                Thread.Sleep(4000);
                try
                {
                    if (!settings.AutoDetect || sessionActive || sessionBusy) continue;
                    var running = new HashSet<string>(Process.GetProcesses().Select(p => p.ProcessName), StringComparer.OrdinalIgnoreCase);
                    if (ignoredProc != null && !running.Contains(ignoredProc)) ignoredProc = null;
                    Game g = games.Games.ToList().FirstOrDefault(x => !string.IsNullOrEmpty(x.Proc) && running.Contains(x.Proc) && !string.Equals(x.Proc, ignoredProc, StringComparison.OrdinalIgnoreCase));
                    string proc = g != null ? g.Proc : KnownGames.Keys.FirstOrDefault(k => running.Contains(k) && !string.Equals(k, ignoredProc, StringComparison.OrdinalIgnoreCase));
                    if (proc == null) continue;
                    var gg = g; var pp = proc;
                    Ui(() => { if (!sessionActive && !sessionBusy) { Log(Tf("{0} detectado: activando Modo Juego", gg != null ? gg.Name : GameName(pp))); StartSession(gg, false, pp); } });
                    Thread.Sleep(6000);
                }
                catch { }
            }
        }

        void Play(Game g)
        {
            if (string.IsNullOrWhiteSpace(g.Target)) { Toast(Tr("Este juego no tiene ejecutable o URL. Edítalo primero."), 3); OpenEditor(g); return; }
            if (sessionBusy) { Toast(Tr("Espera, estoy preparando la sesión..."), 1); return; }
            if (sessionActive)
            {
                sessionGame = g; sessionName = g.Name; sessionProc = string.IsNullOrEmpty(g.Proc) ? null : g.Proc;
                Bg(() => LaunchGame(g));
                return;
            }
            StartSession(g, true, null);
        }

        void SetSessionUi(bool active)
        {
            E<Shapes.Ellipse>("SessDot").Fill = active ? Br("Ok") : Br("Mute");
            E<TextBlock>("SessTitle").Text = active ? Tr("Modo Juego activo") : Tr("Modo Juego inactivo");
            E<TextBlock>("SessSub").Text = active ? (sessionName ?? Tr("recién activado")) : Tr("Pulsa Jugar en cualquier juego");
            E<TextBlock>("GameModeText").Text = active ? Tr("Desactivar Modo Juego") : Tr("Activar Modo Juego");
            E<TextBlock>("GameModeIcon").Text = active ? "" : "";
            var b = E<Button>("BtnGameMode");
            b.Background = active ? Hex("#1F2A22") : Br("AccentGrad");
            b.BorderBrush = active ? Hex("#2E5C3E") : null;
            b.BorderThickness = new Thickness(active ? 1 : 0);
            if (tray != null)
            {
                trayModeItem.Text = active ? Tr("Desactivar Modo Juego") : Tr("Activar Modo Juego");
                string tt = active ? "GameBoost · " + Tr("Modo Juego activo") : "GameBoost";
                tray.Text = tt.Length > 63 ? tt.Substring(0, 63) : tt;
            }
        }

        void StartSession(Game g, bool launch, string runningProc)
        {
            if (sessionBusy || sessionActive) return;
            sessionBusy = true;
            E<Button>("BtnGameMode").IsEnabled = false;
            var s = settings;
            string kill = s.KillList, svcs = s.Services;
            if (sys != null && sys.DualX3D) kill = SessionOps.Without(kill, "GameBar");
            if (sys != null && sys.IsHdd) svcs = SessionOps.Without(svcs, "SysMain");
            string name = g != null ? g.Name : runningProc != null ? GameName(runningProc) : null;
            string proc = g != null && !string.IsNullOrEmpty(g.Proc) ? g.Proc : runningProc;
            string prio = g != null ? (g.Priority ?? "High") : "High";
            Toast(name != null ? Tf("Preparando tu PC para {0}...", name) : Tr("Activando Modo Juego..."), 2, true);
            Bg(() =>
            {
                var summary = new List<string>();
                try
                {
                    if (s.AutoTweaks)
                    {
                        int n = 0;
                        foreach (var t in Tweaks.All.Where(x => x.Rec).ToList())
                            if (!t.SafeCheck()) { try { t.Apply(); n++; if (t.Reboot) rebootPending = true; } catch { } }
                        if (n > 0) summary.Add(Tf("{0} ajustes", n));
                    }
                    if (s.CleanTemp)
                    {
                        long f = 0;
                        foreach (var id in new[] { "temp", "wintemp", "dumps" }) f += Cleaner.Clean(Cleaner.Get(id));
                        if (f > 0) summary.Add(Tf("{0} limpiados", Sys.Fmt(f)));
                    }
                    if (s.FlushDns) Sys.Run("ipconfig", "/flushdns");
                    if (s.CloseApps)
                    {
                        int cnt;
                        closedApps = SessionOps.KillApps(kill, out cnt);
                        if (cnt > 0) summary.Add(Tf("{0} apps cerradas", cnt));
                    }
                    else closedApps = new List<string>();
                    stoppedSvcs = s.PauseServices ? SessionOps.StopServices(svcs) : new List<string>();
                    if (stoppedSvcs.Count > 0) summary.Add(Tf("{0} servicios pausados", stoppedSvcs.Count));
                    if (s.PurgeRam) { Sys.PurgeStandby(); summary.Add(Tr("RAM liberada")); }
                    if (s.Timer) { uint cur; Native.NtSetTimerResolution(5000, true, out cur); timerHeld = true; }
                    sessionStart = DateTime.Now; sessionGame = g; sessionName = name; sessionProc = proc;
                    sessionActive = true;
                    string sum = summary.Count > 0 ? string.Join(" · ", summary) : "";
                    Log(Tr("Modo Juego activado") + (sum != "" ? ": " + sum : ""));
                    Ui(() =>
                    {
                        sessionBusy = false;
                        E<Button>("BtnGameMode").IsEnabled = true;
                        SetSessionUi(true);
                        Toast(Tr("Modo Juego activo") + (sum != "" ? " · " + sum : ""), 0);
                        if (runningProc != null || !win.IsVisible) Balloon(name != null ? Tf("Modo Juego activado para {0}", name) : Tr("Modo Juego activo"), sum);
                    });
                    RefreshTweakStates();
                    if (launch && g != null) LaunchGame(g);
                    else if (runningProc != null) WatchProc(runningProc, prio, g, false);
                }
                catch (Exception ex)
                {
                    Ui(() => { sessionBusy = false; E<Button>("BtnGameMode").IsEnabled = true; Toast("Error: " + ex.Message, 3); });
                }
            });
        }

        // GameBoost corre como administrador; Epic/Fortnite (EAC) fallan si se lanzan elevados.
        // explorer.exe abre el destino con los permisos del usuario normal.
        static void LaunchUnelevated(string target, string args)
        {
            string open = target;
            if (!string.IsNullOrWhiteSpace(args) && File.Exists(target))
            {
                // explorer no acepta argumentos: se pasan mediante un acceso directo temporal
                string lnk = Path.Combine(Path.GetTempPath(), "gb_launch_" + Guid.NewGuid().ToString("N") + ".lnk");
                var shellT = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(shellT);
                object sc = shellT.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                var scT = sc.GetType();
                scT.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                scT.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { args });
                scT.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                scT.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                open = lnk;
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(30000); try { File.Delete(lnk); } catch { } });
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + open + "\"") { UseShellExecute = false });
        }

        void LaunchGame(Game g)
        {
            try
            {
                Sys.Ifeo(g.Proc, g.Priority ?? "High");
                if (Sys.GpuPref(g.Icon) | Sys.GpuPref(g.Target)) Log(Tf("GPU de alto rendimiento asignada a {0}", g.Name));
                LaunchUnelevated(g.Target, g.Args);
                Log(Tf("Lanzando {0}", g.Name));
                Ui(() =>
                {
                    g.LastPlayed = NowSec(); g.Plays++;
                    Store.Save("games.json", games);
                    RebuildQuick(); RebuildGames();
                    E<TextBlock>("SessSub").Text = Tf("Abriendo {0}...", g.Name);
                    if (settings.Minimize) win.WindowState = WindowState.Minimized;
                });
                if (!string.IsNullOrWhiteSpace(g.Proc)) WatchProc(g.Proc, g.Priority ?? "High", g, true);
            }
            catch (Exception ex)
            {
                Log(Tf("No se pudo abrir {0}", g.Name) + ": " + ex.Message);
                Toast(Tf("No se pudo abrir {0}", g.Name) + ": " + ex.Message, 3);
            }
        }

        void WatchProc(string proc, string prioStr, Game g, bool waitStart)
        {
            var prio = prioStr == "Normal" ? ProcessPriorityClass.Normal : prioStr == "AboveNormal" ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.High;
            if (waitStart)
            {
                int i = 0;
                for (; i < 150 && !closing; i++)
                {
                    if (Process.GetProcessesByName(proc).Length > 0) break;
                    Thread.Sleep(2000);
                }
                if (i >= 150) { Log(Tf("No se detectó {0}.exe en 5 minutos", proc)); return; }
                Thread.Sleep(6000);
            }
            else Thread.Sleep(1500);
            Sys.Ifeo(proc, prioStr);
            foreach (var p in Process.GetProcessesByName(proc))
            {
                try
                {
                    string exe = p.MainModule.FileName;
                    if (Sys.GpuPref(exe)) Log(Tf("GPU dedicada asignada a {0} (se aplica desde la próxima vez que abras el juego)", Path.GetFileName(exe)));
                    if (g != null && g.Icon == null) { g.Icon = exe; Ui(() => { Store.Save("games.json", games); RebuildGames(); RebuildQuick(); }); }
                }
                catch { }
                try { if (p.PriorityClass != prio) p.PriorityClass = prio; Log(Tf("Prioridad {0} aplicada a {1}.exe", prio, proc)); }
                catch { Log(Tf("Prioridad de {0} aplicada por registro (el anti-cheat bloquea el cambio en vivo)", proc)); }
            }
            string nm = g != null ? g.Name : GameName(proc);
            Ui(() => { if (sessionActive) E<TextBlock>("SessSub").Text = Tf("Jugando {0}", nm); });
            int gone = 0;
            while (!closing && sessionActive && sessionProc == proc)
            {
                Thread.Sleep(3000);
                if (Process.GetProcessesByName(proc).Length > 0) { gone = 0; continue; }
                if (++gone >= 5) break;
            }
            if (closing || !sessionActive || sessionProc != proc) return;
            Log(Tf("{0} cerrado tras {1} min", nm, (int)(DateTime.Now - sessionStart).TotalMinutes));
            if (settings.RestoreOnExit) Ui(() => EndSession(false));
        }

        void EndSession(bool manual)
        {
            if (!sessionActive) return;
            if (manual && sessionProc != null && Process.GetProcessesByName(sessionProc).Length > 0) ignoredProc = sessionProc;
            sessionActive = false;
            var svcs = stoppedSvcs; var apps = closedApps; bool reopen = settings.ReopenApps;
            int mins = (int)(DateTime.Now - sessionStart).TotalMinutes;
            sessionGame = null; sessionName = null; sessionProc = null;
            SetSessionUi(false);
            Bg(() =>
            {
                SessionOps.StartServices(svcs);
                if (timerHeld) { uint cur; Native.NtSetTimerResolution(5000, false, out cur); timerHeld = false; }
                if (reopen)
                    foreach (var a in apps) { try { Process.Start(new ProcessStartInfo(a) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized }); } catch { } }
                stoppedSvcs = new List<string>(); closedApps = new List<string>();
                string msg = Tf("Modo Juego desactivado · sesión de {0} min", mins) + (svcs.Count > 0 ? " · " + Tr("servicios reanudados") : "");
                Log(msg);
                Toast(msg, 1);
                if (!manual && !win.Dispatcher.Invoke(() => win.IsVisible)) Balloon(Tr("Modo Juego desactivado"), msg);
            });
        }

        // ============================================================ self test
        public void RunSelfTest()
        {
            Action<string> snap = n =>
            {
                var rtb = new RenderTargetBitmap((int)win.ActualWidth, (int)win.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(win);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(Path.Combine(Store.Dir, "shot_" + (L.En ? "en_" : "") + n + ".png"))) enc.Save(fs);
            };
            var steps = new List<Action>
            {
                () => {}, () => {}, () => {}, () => {}, () => snap("dash"),
                () => Nav("Games"), () => snap("games"),
                () => Nav("Tweaks"), () => snap("tweaks"),
                () => Nav("Bench"), () => snap("bench"),
                () => Nav("Apps"), () => {}, () => snap("apps"),
                () => Nav("Tips"), () => snap("tips"),
                () => Nav("Settings"), () => snap("settings"),
                () => { var sv = E<ScrollViewer>("PageSettings"); sv.ScrollToVerticalOffset(900); }, () => snap("settings2"),
                () => OpenEditor(games.Games.FirstOrDefault()), () => snap("editor"), () => CloseEditor(), () => Nav("Dash")
            };
            int i = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (s, e) =>
            {
                if (i < steps.Count) { steps[i++](); return; }
                t.Stop();
                File.WriteAllText(Path.Combine(Store.Dir, "selftest.txt"),
                    "games=" + games.Games.Count + "\r\nprofile=" + string.Join(" | ", Chips()) +
                    "\r\nbackup=" + Backup.Count + "\r\ninstalled=" + Installer.RunningInstalled +
                    "\r\nmissing=\r\n" + string.Join("\r\n", L.Missing.OrderBy(x => x)) +
                    "\r\nlog=\r\n" + string.Join("\r\n", logLines));
                win.Close();
            };
            t.Start();
        }
    }
}
