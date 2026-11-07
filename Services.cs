using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace GameBoost
{
    // =====================================================================  BACKUP DE LA CONFIGURACIÓN ORIGINAL
    public class BackupVal { public bool Exists; public string Kind, Data; }
    public class BackupData
    {
        public long Created;
        public string PowerScheme;
        public Dictionary<string, BackupVal> Values = new Dictionary<string, BackupVal>();
    }

    static class Backup
    {
        static readonly object lk = new object();
        static BackupData data;
        public static bool Enabled = true;

        public static BackupData Data
        {
            get { lock (lk) { if (data == null) data = Store.Load<BackupData>("backup.json"); if (data.Values == null) data.Values = new Dictionary<string, BackupVal>(); return data; } }
        }

        static void Save() { Store.Save("backup.json", data); }

        // Guarda el valor ORIGINAL solo la primera vez que GameBoost lo toca
        public static void Capture(RegistryKey root, string path, string name)
        {
            if (!Enabled) return;
            string key = (root == Registry.LocalMachine ? "HKLM" : "HKCU") + "|" + path + "|" + name;
            var d = Data;
            lock (lk)
            {
                if (d.Values.ContainsKey(key)) return;
                var v = new BackupVal();
                try
                {
                    using (var k = root.OpenSubKey(path))
                    {
                        object o = k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (o != null)
                        {
                            v.Exists = true;
                            v.Kind = k.GetValueKind(name).ToString();
                            v.Data = o is string[] ? string.Join("\n", (string[])o) : o is byte[] ? Convert.ToBase64String((byte[])o) : Convert.ToString(o);
                        }
                    }
                }
                catch { }
                if (d.Created == 0) d.Created = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
                d.Values[key] = v;
                Save();
            }
        }

        public static void CapturePower(string scheme)
        {
            if (!Enabled || string.IsNullOrEmpty(scheme)) return;
            var d = Data;
            lock (lk)
            {
                if (d.PowerScheme != null) return;
                d.PowerScheme = scheme;
                if (d.Created == 0) d.Created = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
                Save();
            }
        }

        public static int Count { get { return Data.Values.Count + (Data.PowerScheme != null ? 1 : 0); } }

        // Devuelve el número de valores restaurados
        public static int Restore()
        {
            var d = Data;
            Enabled = false;
            int n = 0;
            var dnsIfaces = new List<string>();
            try
            {
                foreach (var kv in d.Values.ToList())
                {
                    var parts = kv.Key.Split('|');
                    if (parts.Length != 3) continue;
                    var root = parts[0] == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                    string path = parts[1], name = parts[2];
                    try
                    {
                        if (!kv.Value.Exists)
                        {
                            using (var k = root.OpenSubKey(path, true)) if (k != null) k.DeleteValue(name, false);
                        }
                        else
                        {
                            var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), kv.Value.Kind);
                            object val;
                            switch (kind)
                            {
                                case RegistryValueKind.DWord: val = unchecked((int)long.Parse(kv.Value.Data)); break;
                                case RegistryValueKind.QWord: val = long.Parse(kv.Value.Data); break;
                                case RegistryValueKind.MultiString: val = kv.Value.Data.Split('\n'); break;
                                case RegistryValueKind.Binary: val = Convert.FromBase64String(kv.Value.Data); break;
                                default: val = kv.Value.Data; break;
                            }
                            using (var k = root.CreateSubKey(path)) k.SetValue(name, val, kind);
                        }
                        n++;
                        if (name == "NameServer") dnsIfaces.Add(path);
                    }
                    catch { }
                }

                // Aplicar en vivo lo que Windows no relee del registro
                Func<string, int, int> num = (v, def) => { int r; return int.TryParse(Sys.Sz(Registry.CurrentUser, @"Control Panel\Mouse", v), out r) ? r : def; };
                Native.SystemParametersInfo(0x0004, 0, new[] { num("MouseThreshold1", 6), num("MouseThreshold2", 10), num("MouseSpeed", 1) }, 0x3);
                int kd; if (!int.TryParse(Sys.Sz(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay"), out kd)) kd = 1;
                Native.SystemParametersInfoPtr(0x17, (uint)kd, IntPtr.Zero, 3);
                if (d.PowerScheme != null) { Sys.Run("powercfg", "/setactive " + d.PowerScheme); n++; }
                if (Sys.Dw(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start") != 4) Sys.Run("sc", "start DiagTrack");
                foreach (var p in dnsIfaces)
                {
                    string guid = p.Substring(p.LastIndexOf('\\') + 1);
                    string ns = Sys.Sz(Registry.LocalMachine, p, "NameServer") ?? "";
                    string cmd = ns.Trim() == "" ? "-ResetServerAddresses" : "-ServerAddresses " + string.Join(",", ns.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    Sys.Run("powershell", "-NoProfile -Command \"Get-NetAdapter | ? InterfaceGuid -eq '" + guid + "' | Set-DnsClientServerAddress " + cmd + "\"");
                }
                Sys.Run("ipconfig", "/flushdns");
            }
            finally { Enabled = true; }
            return n;
        }
    }

    // =====================================================================  INSTALADOR / DESINSTALADOR
    static class Installer
    {
        public const string Version = "2.2.0";
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BLAD Studios", "GameBoost");
        public static string ExePath { get { return Path.Combine(InstallDir, "GameBoost.exe"); } }
        const string UninstKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BLADStudios.GameBoost";
        public static string StartMenuDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "BLAD Studios"); } }
        public static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "GameBoost.lnk"); } }
        public static bool IsInstalled { get { return File.Exists(ExePath); } }
        public static string CurrentExe { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        public static bool RunningInstalled
        {
            get { try { return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
        }

        public static void Shortcut(string lnk, string target, string args, string desc)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            var lt = l.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, l, new object[] { target });
            lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, l, new object[] { args });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, l, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, l, new object[] { desc });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, l, new object[] { target + ",0" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, l, null);
        }

        static void KillOthers()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("GameBoost"))
            {
                try { if (p.Id != me) { p.Kill(); p.WaitForExit(5000); } } catch { }
            }
        }

        public static void Install(Func<Stream> exeData, bool desktop, bool autostart, Action<string> step)
        {
            step(L.T("Cerrando GameBoost..."));
            KillOthers();
            step(L.T("Copiando archivos..."));
            Directory.CreateDirectory(InstallDir);
            for (int i = 0; ; i++)
            {
                try
                {
                    using (var src = exeData())
                    using (var dst = File.Create(ExePath)) src.CopyTo(dst);
                    break;
                }
                catch { if (i >= 12) throw; Thread.Sleep(500); }
            }
            step(L.T("Creando accesos directos..."));
            Directory.CreateDirectory(StartMenuDir);
            Shortcut(Path.Combine(StartMenuDir, "GameBoost.lnk"), ExePath, "", "GameBoost by BLAD Studios");
            if (desktop) Shortcut(DesktopLnk, ExePath, "", "GameBoost by BLAD Studios");
            step(L.T("Registrando en Windows..."));
            using (var k = Registry.LocalMachine.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", "GameBoost");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "BLAD Studios");
                k.SetValue("DisplayIcon", ExePath + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + ExePath + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + ExePath + "\" --uninstall");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)(new FileInfo(ExePath).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            if (autostart) SetAutostart(true, ExePath);
            step(L.T("Instalación completada"));
        }

        public static void Uninstall(bool restore)
        {
            KillOthers();
            if (restore) Backup.Restore();
            SetAutostart(false);
            if (Tweaks.CurrentScheme() != Tweaks.MyPlan) Sys.Run("powercfg", "/delete " + Tweaks.MyPlan);
            try { File.Delete(DesktopLnk); } catch { }
            try { File.Delete(Path.Combine(StartMenuDir, "GameBoost.lnk")); if (!Directory.EnumerateFileSystemEntries(StartMenuDir).Any()) Directory.Delete(StartMenuDir); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstKey, false); } catch { }
            if (restore) { try { Directory.Delete(Store.Dir, true); } catch { } }
            string parent = Path.GetDirectoryName(InstallDir);
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"" + InstallDir + "\" & rmdir \"" + parent + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        public static void SetAutostart(bool on, string exe = null)
        {
            if (!on) { Sys.Run("schtasks", "/delete /tn \"\\BLAD Studios\\GameBoost\" /f"); return; }
            if (exe == null) exe = IsInstalled ? ExePath : CurrentExe;
            Sys.RunPs(
                "$u = \"$env:USERDOMAIN\\$env:USERNAME\"\n" +
                "$a = New-ScheduledTaskAction -Execute '" + exe.Replace("'", "''") + "' -Argument '--tray'\n" +
                "$t = New-ScheduledTaskTrigger -AtLogOn -User $u\n" +
                "$p = New-ScheduledTaskPrincipal -UserId $u -RunLevel Highest -LogonType Interactive\n" +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0\n" +
                "Register-ScheduledTask -TaskName 'GameBoost' -TaskPath '\\BLAD Studios\\' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null\n");
        }

        public static bool AutostartEnabled() { return Sys.RunCode("schtasks", "/query /tn \"\\BLAD Studios\\GameBoost\"") == 0; }
    }

    // =====================================================================  ACTUALIZACIONES
    public class UpdateInfo { public string version, url, notes; }

    static class Updater
    {
        // Sube version.json (generado por build.ps1 en la carpeta dist) a esta dirección para publicar una actualización
        public const string Feed = "https://raw.githubusercontent.com/bladstudios/gameboost/main/version.json";

        public static bool Newer(string a, string b)
        {
            Version va, vb;
            return Version.TryParse(a, out va) && Version.TryParse(b, out vb) && va > vb;
        }

        public static UpdateInfo Check()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "GameBoost/" + Installer.Version;
                string s = wc.DownloadString(Feed + "?t=" + DateTime.UtcNow.Ticks);
                var u = new JavaScriptSerializer().Deserialize<UpdateInfo>(s);
                return u != null && Newer(u.version, Installer.Version) && !string.IsNullOrEmpty(u.url) ? u : null;
            }
        }

        public static string Download(UpdateInfo u, Action<int> progress)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string f = Path.Combine(Path.GetTempPath(), "GameBoost_Setup_" + u.version + ".exe");
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "GameBoost/" + Installer.Version;
                wc.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                wc.DownloadFileTaskAsync(new Uri(u.url), f).Wait();
            }
            return f;
        }
    }

    // =====================================================================  BENCHMARK
    public class BenchResult
    {
        public long Time;
        public double Single, Multi, TimerMs, IdleCpu, RamFreeGB, Ping, Jitter;
        public int Procs, Score;
        public bool GameMode;
    }
    public class BenchList { public List<BenchResult> Items = new List<BenchResult>(); }

    static class Bench
    {
        static long Work(int ms)
        {
            var sw = Stopwatch.StartNew();
            long it = 0; double x = 1.0001;
            while (sw.ElapsedMilliseconds < ms)
            {
                for (int i = 0; i < 20000; i++) { x = x * 1.0000001 + 0.0000001; if (x > 2) x = 1.0001; }
                it++;
            }
            if (x < 0) it++; // evita que el compilador elimine el bucle
            return it;
        }

        public static BenchResult Run(Action<int, string> progress, bool gameMode)
        {
            var r = new BenchResult { Time = DateTime.Now.Ticks / TimeSpan.TicksPerSecond, GameMode = gameMode };

            progress(5, L.T("Midiendo carga en segundo plano..."));
            long i1, k1, u1, i2, k2, u2;
            Native.GetSystemTimes(out i1, out k1, out u1);
            Thread.Sleep(2500);
            Native.GetSystemTimes(out i2, out k2, out u2);
            long dt = (k2 - k1) + (u2 - u1);
            r.IdleCpu = dt > 0 ? Math.Round((1.0 - (double)(i2 - i1) / dt) * 100, 1) : 0;
            r.Procs = Process.GetProcesses().Length;
            var m = new Native.MEMORYSTATUSEX(); Native.GlobalMemoryStatusEx(m);
            r.RamFreeGB = Math.Round(m.ullAvailPhys / 1073741824.0, 1);

            progress(25, L.T("Midiendo precisión del temporizador..."));
            var sw = new Stopwatch(); double tot = 0;
            for (int i = 0; i < 60; i++) { sw.Restart(); Thread.Sleep(1); tot += sw.Elapsed.TotalMilliseconds; }
            r.TimerMs = Math.Round(tot / 60, 2);

            progress(40, L.T("CPU: un núcleo..."));
            long single = 0;
            var t = new Thread(() => single = Work(3000)) { Priority = ThreadPriority.AboveNormal };
            t.Start(); t.Join();
            r.Single = Math.Round(single / 10.0, 1);

            progress(65, L.T("CPU: todos los núcleos..."));
            int n = Environment.ProcessorCount;
            var counts = new long[n];
            var ths = new Thread[n];
            for (int i = 0; i < n; i++) { int idx = i; ths[i] = new Thread(() => counts[idx] = Work(3000)); ths[i].Start(); }
            foreach (var th in ths) th.Join();
            r.Multi = Math.Round(counts.Sum() / 10.0, 1);

            progress(90, L.T("Midiendo red..."));
            var pings = new List<long>();
            using (var p = new Ping())
                for (int i = 0; i < 10; i++)
                {
                    try { var rep = p.Send("1.1.1.1", 1000); if (rep.Status == IPStatus.Success) pings.Add(rep.RoundtripTime); } catch { }
                    Thread.Sleep(120);
                }
            r.Ping = pings.Count > 0 ? Math.Round(pings.Average(), 1) : -1;
            r.Jitter = pings.Count > 1 ? Math.Round(pings.Zip(pings.Skip(1), (a, b) => (double)Math.Abs(a - b)).Average(), 1) : 0;

            r.Score = (int)Math.Round(r.Multi * (1 - Math.Min(r.IdleCpu, 90) / 100.0) / 8 + r.Single);
            progress(100, "");
            return r;
        }
    }
}
