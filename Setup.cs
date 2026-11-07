using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: AssemblyTitle("GameBoost Setup")]
[assembly: AssemblyDescription("Instalador de GameBoost")]
[assembly: AssemblyCompany("BLAD Studios")]
[assembly: AssemblyProduct("GameBoost")]
[assembly: AssemblyCopyright("© 2026 BLAD Studios")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]
[assembly: AssemblyInformationalVersion("2.2.0")]

namespace GameBoost
{
    static class SetupProgram
    {
        static Stream Payload() { return Assembly.GetExecutingAssembly().GetManifestResourceStream("GameBoost.exe"); }

        [STAThread]
        static int Main(string[] args)
        {
            var a = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
            L.Init(a.Contains("--lang-en") ? "en" : Store.Load<Settings>("settings.json").Lang);
            bool selfTest = a.Contains("--selftest");

            // Actualización silenciosa: la lanza GameBoost al pulsar "Actualizar ahora"
            if (a.Contains("--update") || a.Contains("--silent"))
            {
                try
                {
                    bool desktop = File.Exists(Installer.DesktopLnk) || !Installer.IsInstalled;
                    Installer.Install(Payload, desktop, false, s => { });
                    Process.Start(new ProcessStartInfo(Installer.ExePath) { UseShellExecute = true });
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "GameBoost Setup"); return 1; }
            }

            var app = new Application();
            Window w;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("Setup.xaml")) w = (Window)XamlReader.Load(st);
            try { w.Icon = BitmapFrame.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")); } catch { }
            L.Translate(w);

            var btn = (Button)w.FindName("BtnInstall");
            var step = (TextBlock)w.FindName("StepText");
            var bar = (Border)w.FindName("Bar");
            var chkDesk = (CheckBox)w.FindName("ChkDesktop");
            var chkAuto = (CheckBox)w.FindName("ChkStartup");
            ((TextBlock)w.FindName("InstallPath")).Text = Installer.InstallDir;
            ((TextBlock)w.FindName("SetupVersion")).Text = "· v" + Installer.Version;
            ((Button)w.FindName("BtnX")).Click += (s, e) => w.Close();
            w.MouseLeftButtonDown += (s, e) => { try { w.DragMove(); } catch { } };
            if (Installer.IsInstalled)
            {
                btn.Content = L.T("Reinstalar / actualizar");
                step.Text = L.T("Ya está instalado. Puedes reinstalar o actualizar.");
            }

            bool done = false;
            btn.Click += (s, e) =>
            {
                if (done) { Process.Start(new ProcessStartInfo(Installer.ExePath) { UseShellExecute = true }); w.Close(); return; }
                bool desk = chkDesk.IsChecked == true, auto = chkAuto.IsChecked == true;
                btn.IsEnabled = false; chkDesk.IsEnabled = false; chkAuto.IsEnabled = false;
                step.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0xA2));
                int n = 0;
                double full = ((FrameworkElement)bar.Parent).ActualWidth;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Installer.Install(Payload, desk, auto, msg => w.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            step.Text = msg; n++;
                            bar.BeginAnimation(FrameworkElement.WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(full * Math.Min(1, n / 5.0), TimeSpan.FromMilliseconds(300)));
                        })));
                        w.Dispatcher.BeginInvoke(new Action(() => { done = true; btn.IsEnabled = true; btn.Content = L.T("Abrir GameBoost ahora"); }));
                    }
                    catch (Exception ex)
                    {
                        w.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            step.Text = L.T("Error durante la instalación") + ": " + ex.Message;
                            step.Foreground = Brushes.OrangeRed;
                            btn.IsEnabled = true; chkDesk.IsEnabled = true; chkAuto.IsEnabled = true;
                        }));
                    }
                });
            };

            if (selfTest)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -20000; w.Top = -20000; w.ShowActivated = false; w.ShowInTaskbar = false;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    var rtb = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(w);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                    Directory.CreateDirectory(Store.Dir);
                    using (var fs = File.Create(Path.Combine(Store.Dir, "shot_setup" + (L.En ? "_en" : "") + ".png"))) enc.Save(fs);
                    w.Close();
                };
                t.Start();
            }
            app.Run(w);
            return 0;
        }
    }
}
