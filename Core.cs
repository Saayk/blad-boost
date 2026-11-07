using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace GameBoost
{
    // =====================================================================  NATIVE
    static class Native
    {
        [DllImport("ntdll.dll")] public static extern int NtSetTimerResolution(uint desired, bool set, out uint current);
        [DllImport("ntdll.dll")] public static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [StructLayout(LayoutKind.Sequential)] public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint param, int[] pv, uint winIni);
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfo")] public static extern bool SystemParametersInfoPtr(uint action, uint param, IntPtr pv, uint winIni);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = 64; public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);

        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attr; }
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES st, int len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static bool EnablePrivilege(string name)
        {
            IntPtr tok;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out tok)) return false;
            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 2 };
                return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(tok); }
        }
    }

    // =====================================================================  HELPERS
    static class Sys
    {
        public static RegistryKey CU { get { return Registry.CurrentUser; } }
        public static RegistryKey LM { get { return Registry.LocalMachine; } }

        public static int? Dw(RegistryKey root, string path, string name)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return null;
                    object v = k.GetValue(name);
                    if (v is int) return (int)v;
                    return null;
                }
            }
            catch { return null; }
        }
        public static string Sz(RegistryKey root, string path, string name)
        {
            try { using (var k = root.OpenSubKey(path)) return k == null ? null : k.GetValue(name) as string; }
            catch { return null; }
        }
        // Toda escritura pasa por aquí: antes de cambiar un valor se guarda el original en la copia de seguridad
        public static void SetDw(RegistryKey root, string path, string name, int v) { Backup.Capture(root, path, name); using (var k = root.CreateSubKey(path)) k.SetValue(name, v, RegistryValueKind.DWord); }
        public static void SetSz(RegistryKey root, string path, string name, string v) { Backup.Capture(root, path, name); using (var k = root.CreateSubKey(path)) k.SetValue(name, v, RegistryValueKind.String); }
        public static void Del(RegistryKey root, string path, string name) { Backup.Capture(root, path, name); using (var k = root.OpenSubKey(path, true)) if (k != null) k.DeleteValue(name, false); }

        public static int RunCode(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(60000); return p.ExitCode; }
            }
            catch { return -1; }
        }

        public static string RunPs(string script)
        {
            string f = Path.Combine(Path.GetTempPath(), "gb_" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(f, script, new System.Text.UTF8Encoding(true));
            try { return Run("powershell", "-NoProfile -ExecutionPolicy Bypass -File \"" + f + "\""); }
            finally { try { File.Delete(f); } catch { } }
        }

        public static string Run(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(120000);
                    return o + err.Result;
                }
            }
            catch (Exception ex) { return "ERROR: " + ex.Message; }
        }

        public static string Fmt(long b)
        {
            if (b < 0) return "—";
            if (b >= 1L << 30) return (b / 1073741824.0).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / 1048576.0).ToString("0") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        public static void PurgeStandby()
        {
            Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
            int cmd = 4;
            Native.NtSetSystemInformation(80, ref cmd, 4);
        }

        // Fuerza la GPU de alto rendimiento (dedicada) para un ejecutable (Configuración > Pantalla > Gráficos)
        public static bool GpuPref(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exePath)) return false;
                const string k = @"Software\Microsoft\DirectX\UserGpuPreferences";
                string cur = Sz(CU, k, exePath);
                if (cur != null && cur.Contains("GpuPreference=2;")) return false;
                SetSz(CU, k, exePath, "GpuPreference=2;");
                return true;
            }
            catch { return false; }
        }

        public static void Ifeo(string proc, string prio)
        {
            if (string.IsNullOrWhiteSpace(proc)) return;
            string exe = proc.Trim();
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + exe + @"\PerfOptions";
            int v = prio == "High" ? 3 : prio == "AboveNormal" ? 6 : 0;
            try
            {
                Backup.Capture(LM, path, "CpuPriorityClass");
                if (v == 0) LM.DeleteSubKeyTree(path, false);
                else SetDw(LM, path, "CpuPriorityClass", v);
            }
            catch { }
        }

        public static List<string> InstalledApps()
        {
            var l = new List<string>();
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var p in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    try
                    {
                        using (var k = root.OpenSubKey(p))
                        {
                            if (k == null) continue;
                            foreach (var n in k.GetSubKeyNames())
                                using (var s = k.OpenSubKey(n))
                                {
                                    var d = s == null ? null : s.GetValue("DisplayName") as string;
                                    if (!string.IsNullOrEmpty(d)) l.Add(d);
                                }
                        }
                    }
                    catch { }
                }
            return l;
        }
    }

    static class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBoost");
        public static T Load<T>(string name) where T : new()
        {
            try
            {
                string p = Path.Combine(Dir, name);
                if (File.Exists(p)) { var o = new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(p)); if (o != null) return o; }
            }
            catch { }
            return new T();
        }
        public static void Save(string name, object o)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, name), new JavaScriptSerializer().Serialize(o)); } catch { }
        }
    }

    // =====================================================================  MODELS
    public class Game
    {
        public string Id, Name, Source, Target, Args, Proc, Priority, Image, Icon;
        public long LastPlayed;
        public int Plays;
    }
    public class GameList { public List<Game> Games = new List<Game>(); }

    public class Settings
    {
        public bool AutoTweaks = true, CleanTemp = true, PurgeRam = true, FlushDns = true, CloseApps = true, PauseServices = true,
                    Timer = true, RestoreOnExit = true, ReopenApps = true, Minimize = true, Adapted,
                    AutoDetect = true, TrayOnClose = true, CheckUpdates = true, TrayHintShown;
        public string Lang = "auto", SkipVersion = "";
        public int BaselineIdx = -1;
        public string KillList = "OneDrive\r\nms-teams\r\nTeams\r\nPhoneExperienceHost\r\nWidgets\r\nWidgetService\r\nGameBar";
        public string Services = "SysMain\r\nWSearch\r\nDiagTrack\r\nwuauserv\r\nBITS\r\nDoSvc";
        public List<string> CleanSel;
        public long LastClean;
        public long LastCleanBytes;
    }

    // =====================================================================  TWEAKS
    public class Tweak
    {
        public string Id, Cat, Title, Desc;
        public int Impact;
        public bool Rec, Reboot, State;
        public Func<bool> Check;
        public Action Apply, Revert;
        public bool SafeCheck() { try { return Check(); } catch { return false; } }
    }

    static class Tweaks
    {
        public const string MyPlan = "6e2f9b1c-4d3a-4f7e-9b8a-2c5d7e1f0a93";
        const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-cf5f5a4e5a5c";
        const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        const string IfKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        const string MM = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string GCS = @"System\GameConfigStore";
        const string DX = @"Software\Microsoft\DirectX\UserGpuPreferences";

        public static readonly List<Tweak> All = new List<Tweak>();
        public static readonly string[] Cats = { "Energía y CPU", "Gráficos y juego", "Red", "Periféricos", "Sistema" };

        public static Tweak Get(string id) { return All.First(t => t.Id == id); }

        static void Add(string id, string cat, string title, string desc, int impact, bool rec, bool reboot, Func<bool> check, Action apply, Action revert)
        {
            All.Add(new Tweak { Id = id, Cat = cat, Title = L.T(title), Desc = L.T(desc), Impact = impact, Rec = rec, Reboot = reboot, Check = check, Apply = apply, Revert = revert });
        }

        public static string CurrentScheme() { return ActiveScheme(); }

        static string ActiveScheme()
        {
            var m = Regex.Match(Sys.Run("powercfg", "/getactivescheme"), "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value.ToLowerInvariant() : "";
        }

        static void PowerPlanOn()
        {
            Backup.CapturePower(ActiveScheme());
            string list = Sys.Run("powercfg", "/list");
            if (list.IndexOf(MyPlan, StringComparison.OrdinalIgnoreCase) < 0)
            {
                Sys.Run("powercfg", "/duplicatescheme " + Ultimate + " " + MyPlan);
                Sys.Run("powercfg", "/changename " + MyPlan + " \"GameBoost Ultimate\" \"Plan de maximo rendimiento creado por GameBoost\"");
                list = Sys.Run("powercfg", "/list");
            }
            if (list.IndexOf(MyPlan, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Sys.Run("powercfg", "/setacvalueindex " + MyPlan + " SUB_PROCESSOR CPMINCORES 100");
                Sys.Run("powercfg", "/setacvalueindex " + MyPlan + " SUB_PROCESSOR PROCTHROTTLEMIN 100");
                Sys.Run("powercfg", "/setacvalueindex " + MyPlan + " 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0");
                Sys.Run("powercfg", "/setactive " + MyPlan);
            }
            else Sys.Run("powercfg", "/setactive " + HighPerf);
        }

        // Rutas de registro de los adaptadores de red con IP asignada
        public static List<string> ActiveIfaces()
        {
            var res = new List<string>();
            using (var k = Sys.LM.OpenSubKey(IfKey))
            {
                if (k == null) return res;
                foreach (var n in k.GetSubKeyNames())
                    using (var s = k.OpenSubKey(n))
                    {
                        if (s == null) continue;
                        string dhcp = s.GetValue("DhcpIPAddress") as string;
                        string[] ip = s.GetValue("IPAddress") as string[];
                        bool has = (!string.IsNullOrEmpty(dhcp) && dhcp != "0.0.0.0") || (ip != null && ip.Length > 0 && !string.IsNullOrEmpty(ip[0]) && ip[0] != "0.0.0.0");
                        if (has) res.Add(IfKey + "\\" + n);
                    }
            }
            return res;
        }

        static bool AnyInterface(Func<RegistryKey, bool> pred)
        {
            foreach (var p in ActiveIfaces())
                using (var k = Sys.LM.OpenSubKey(p)) if (k != null && pred(k)) return true;
            return false;
        }

        static readonly string[] NagleVals = { "TcpAckFrequency", "TCPNoDelay", "TcpDelAckTicks" };
        static void NagleAll(bool off)
        {
            if (off)
            {
                foreach (var p in ActiveIfaces())
                {
                    Sys.SetDw(Sys.LM, p, "TcpAckFrequency", 1);
                    Sys.SetDw(Sys.LM, p, "TCPNoDelay", 1);
                    Sys.SetDw(Sys.LM, p, "TcpDelAckTicks", 0);
                }
                return;
            }
            using (var k = Sys.LM.OpenSubKey(IfKey))
                foreach (var n in k.GetSubKeyNames())
                    foreach (var v in NagleVals)
                        if (Sys.Dw(Sys.LM, IfKey + "\\" + n, v).HasValue) Sys.Del(Sys.LM, IfKey + "\\" + n, v);
        }

        static void CaptureDns() { foreach (var p in ActiveIfaces()) Backup.Capture(Sys.LM, p, "NameServer"); }

        static void KbdDelay(uint v)
        {
            Backup.Capture(Sys.CU, @"Control Panel\Keyboard", "KeyboardDelay");
            Native.SystemParametersInfoPtr(0x17, v, IntPtr.Zero, 3);
        }

        static void DxSetting(string key, string val)
        {
            string cur = Sys.Sz(Sys.CU, DX, "DirectXUserGlobalSettings") ?? "";
            var parts = cur.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith(key + "=")).ToList();
            parts.Add(key + "=" + val);
            Sys.SetSz(Sys.CU, DX, "DirectXUserGlobalSettings", string.Join(";", parts) + ";");
        }

        static void Mouse(bool accel)
        {
            foreach (var v in new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }) Backup.Capture(Sys.CU, @"Control Panel\Mouse", v);
            Native.SystemParametersInfo(0x0004, 0, accel ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 }, 0x3);
        }

        static Tweaks()
        {
            string E = Cats[0], G = Cats[1], N = Cats[2], P = Cats[3], S = Cats[4];

            Add("power", E, "Plan de energía Máximo Rendimiento",
                "Crea un plan Ultimate sin core parking, con la CPU siempre a máxima frecuencia y sin suspensión USB.", 3, true, false,
                () =>
                {
                    var a = ActiveScheme();
                    if (a == MyPlan || a == HighPerf || a == Ultimate) return true;
                    if (a == Balanced || a == "") return false;
                    var hx = Regex.Matches(Sys.Run("powercfg", "/query " + a + " SUB_PROCESSOR PROCTHROTTLEMIN"), "0x[0-9a-fA-F]{8}");
                    return hx.Count >= 2 && Convert.ToInt32(hx[hx.Count - 2].Value, 16) == 100;
                },
                PowerPlanOn,
                () => { Backup.CapturePower(ActiveScheme()); Sys.Run("powercfg", "/setactive " + Balanced); });

            Add("throttle", E, "Desactivar Power Throttling",
                "Evita que Windows reduzca la CPU a procesos en segundo plano (Discord, OBS, overlays).", 2, true, false,
                () => Sys.Dw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff") == 1,
                () => Sys.SetDw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1),
                () => Sys.Del(Sys.LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"));

            Add("prisep", E, "Prioridad de CPU para la ventana activa",
                "Da quantums más cortos y prioridad al programa en primer plano (Win32PrioritySeparation = 0x26).", 2, true, false,
                () => Sys.Dw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation") == 0x26,
                () => Sys.SetDw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26),
                () => Sys.SetDw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2));

            Add("timer", E, "Resolución de timer global",
                "Permite que la resolución de 0.5 ms del Modo Juego afecte a todo el sistema en Windows 11. Mejora la estabilidad del frametime.", 1, false, true,
                () => Sys.Dw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests") == 1,
                () => Sys.SetDw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1),
                () => Sys.Del(Sys.LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests"));

            Add("gamemode", G, "Modo Juego de Windows",
                "Prioriza el juego y evita que Windows Update instale drivers o muestre notificaciones mientras juegas.", 3, true, false,
                () => Sys.Dw(Sys.CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") != 0,
                () => { Sys.SetDw(Sys.CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1); Sys.SetDw(Sys.CU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1); },
                () => Sys.SetDw(Sys.CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 0));

            Add("dvr", G, "Desactivar grabación en segundo plano",
                "Apaga Xbox Game DVR, que graba continuamente y puede costar entre un 3 y un 10% de FPS.", 3, true, false,
                () => Sys.Dw(Sys.CU, GCS, "GameDVR_Enabled") == 0,
                () => { Sys.SetDw(Sys.CU, GCS, "GameDVR_Enabled", 0); Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0); },
                () => { Sys.SetDw(Sys.CU, GCS, "GameDVR_Enabled", 1); Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1); });

            Add("hags", G, "Programación de GPU acelerada por hardware",
                "La GPU gestiona su propia memoria de vídeo: menor latencia y es necesaria para DLSS Frame Generation.", 2, true, true,
                () => Sys.Dw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") == 2,
                () => Sys.SetDw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
                () => Sys.SetDw(Sys.LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1));

            Add("windowed", G, "Optimizaciones para juegos en ventana",
                "Los juegos en ventana sin bordes usan el modelo flip: latencia equivalente a pantalla completa y soporte de VRR.", 2, true, false,
                () => (Sys.Sz(Sys.CU, DX, "DirectXUserGlobalSettings") ?? "").Contains("SwapEffectUpgradeEnable=1"),
                () => DxSetting("SwapEffectUpgradeEnable", "1"),
                () => DxSetting("SwapEffectUpgradeEnable", "0"));

            Add("mmcss", G, "Prioridad de juegos en el programador multimedia",
                "Reserva menos CPU para tareas de fondo (SystemResponsiveness 10) y sube la prioridad de los juegos en MMCSS.", 2, true, false,
                () => { var v = Sys.Dw(Sys.LM, MM, "SystemResponsiveness"); return v.HasValue && v.Value <= 10; },
                () =>
                {
                    Sys.SetDw(Sys.LM, MM, "SystemResponsiveness", 10);
                    string g = MM + @"\Tasks\Games";
                    Sys.SetDw(Sys.LM, g, "GPU Priority", 8); Sys.SetDw(Sys.LM, g, "Priority", 6);
                    Sys.SetSz(Sys.LM, g, "Scheduling Category", "High"); Sys.SetSz(Sys.LM, g, "SFIO Priority", "High");
                },
                () =>
                {
                    Sys.SetDw(Sys.LM, MM, "SystemResponsiveness", 20);
                    string g = MM + @"\Tasks\Games";
                    Sys.SetDw(Sys.LM, g, "GPU Priority", 8); Sys.SetDw(Sys.LM, g, "Priority", 2);
                    Sys.SetSz(Sys.LM, g, "Scheduling Category", "Medium"); Sys.SetSz(Sys.LM, g, "SFIO Priority", "Normal");
                });

            Add("fso", G, "Desactivar optimizaciones de pantalla completa",
                "Fuerza pantalla completa exclusiva real. Ayuda en algunos juegos antiguos; en los modernos suele ser mejor dejarlo.", 1, false, false,
                () => Sys.Dw(Sys.CU, GCS, "GameDVR_FSEBehaviorMode") == 2,
                () =>
                {
                    Sys.SetDw(Sys.CU, GCS, "GameDVR_FSEBehaviorMode", 2); Sys.SetDw(Sys.CU, GCS, "GameDVR_HonorUserFSEBehaviorMode", 1);
                    Sys.SetDw(Sys.CU, GCS, "GameDVR_FSEBehavior", 2); Sys.SetDw(Sys.CU, GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
                },
                () =>
                {
                    Sys.SetDw(Sys.CU, GCS, "GameDVR_FSEBehaviorMode", 0); Sys.SetDw(Sys.CU, GCS, "GameDVR_HonorUserFSEBehaviorMode", 0);
                    Sys.SetDw(Sys.CU, GCS, "GameDVR_FSEBehavior", 0); Sys.SetDw(Sys.CU, GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 0);
                });

            Add("transp", G, "Desactivar transparencias",
                "Quita el efecto de vidrio de Windows. Libera algo de GPU en equipos modestos o con gráficos integrados.", 1, false, false,
                () => Sys.Dw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") == 0,
                () => Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0),
                () => Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1));

            Add("nagle", N, "Desactivar algoritmo de Nagle",
                "Envía los paquetes TCP al instante en lugar de agruparlos. Reduce la latencia en juegos que usan TCP.", 2, true, false,
                () => AnyInterface(k => (k.GetValue("TcpAckFrequency") as int?) == 1),
                () => NagleAll(true),
                () => NagleAll(false));

            Add("netthrottle", N, "Quitar limitación de red multimedia",
                "Desactiva el límite de paquetes de red que Windows aplica cuando se reproduce audio o vídeo.", 1, true, false,
                () => Sys.Dw(Sys.LM, MM, "NetworkThrottlingIndex") == -1,
                () => Sys.SetDw(Sys.LM, MM, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)),
                () => Sys.SetDw(Sys.LM, MM, "NetworkThrottlingIndex", 10));

            Add("dns", N, "DNS rápido (Cloudflare 1.1.1.1)",
                "Cambia el DNS de tus adaptadores a 1.1.1.1 / 1.0.0.1. Acelera la conexión a servidores y matchmaking; no cambia el ping en partida.", 1, false, false,
                () => AnyInterface(k => ((k.GetValue("NameServer") as string) ?? "").Contains("1.1.1.1")),
                () => { CaptureDns(); Sys.Run("powershell", "-NoProfile -Command \"Get-NetAdapter | ? Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses 1.1.1.1,1.0.0.1\""); },
                () => { CaptureDns(); Sys.Run("powershell", "-NoProfile -Command \"Get-NetAdapter | Set-DnsClientServerAddress -ResetServerAddresses\""); });

            Add("mouse", P, "Desactivar aceleración del mouse",
                "Movimiento 1:1: la mira se mueve igual sin importar la velocidad de tu mano. Imprescindible para apuntar.", 3, true, false,
                () => Sys.Sz(Sys.CU, @"Control Panel\Mouse", "MouseSpeed") == "0",
                () => Mouse(false),
                () => Mouse(true));

            Add("kbd", P, "Respuesta de teclado más rápida",
                "Reduce el retardo de repetición de teclas al mínimo.", 1, true, false,
                () => Sys.Sz(Sys.CU, @"Control Panel\Keyboard", "KeyboardDelay") == "0",
                () => KbdDelay(0),
                () => KbdDelay(1));

            Add("sticky", P, "Desactivar atajo de Teclas especiales",
                "Evita la ventana que aparece al pulsar Shift 5 veces en plena partida. Se aplica al reiniciar sesión.", 1, true, true,
                () => Sys.Sz(Sys.CU, @"Control Panel\Accessibility\StickyKeys", "Flags") == "506",
                () =>
                {
                    Sys.SetSz(Sys.CU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506");
                    Sys.SetSz(Sys.CU, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58");
                    Sys.SetSz(Sys.CU, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122");
                },
                () =>
                {
                    Sys.SetSz(Sys.CU, @"Control Panel\Accessibility\StickyKeys", "Flags", "510");
                    Sys.SetSz(Sys.CU, @"Control Panel\Accessibility\ToggleKeys", "Flags", "62");
                    Sys.SetSz(Sys.CU, @"Control Panel\Accessibility\Keyboard Response", "Flags", "126");
                });

            Add("bgapps", S, "Desactivar apps en segundo plano",
                "Impide que las apps de la Tienda se ejecuten en segundo plano consumiendo CPU, RAM y red.", 2, true, false,
                () => Sys.Dw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == 1,
                () => { Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1); Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0); },
                () => { Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0); Sys.SetDw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 1); });

            Add("telemetry", S, "Desactivar telemetría (DiagTrack)",
                "Detiene el servicio que envía datos de uso a Microsoft en segundo plano.", 1, true, false,
                () => Sys.Dw(Sys.LM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start") == 4,
                () => { Backup.Capture(Sys.LM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"); Sys.Run("sc", "config DiagTrack start= disabled"); Sys.Run("sc", "stop DiagTrack"); },
                () => { Backup.Capture(Sys.LM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"); Sys.Run("sc", "config DiagTrack start= auto"); Sys.Run("sc", "start DiagTrack"); });

            Add("tips", S, "Desactivar sugerencias y consejos de Windows",
                "Quita notificaciones de 'sugerencias', anuncios y consejos que pueden aparecer en medio de una partida.", 1, true, false,
                () => Sys.Dw(Sys.CU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled") == 0,
                () =>
                {
                    string c = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    foreach (var v in new[] { "SubscribedContent-338389Enabled", "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SoftLandingEnabled", "SystemPaneSuggestionsEnabled" })
                        Sys.SetDw(Sys.CU, c, v, 0);
                },
                () =>
                {
                    string c = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    foreach (var v in new[] { "SubscribedContent-338389Enabled", "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SoftLandingEnabled", "SystemPaneSuggestionsEnabled" })
                        Sys.SetDw(Sys.CU, c, v, 1);
                });
        }
    }

    // =====================================================================  CLEANER
    public class CleanItem
    {
        public string Id, Name, Desc;
        public bool Def;
        public int Kind;          // 0 = carpetas, 1 = papelera, 2 = DNS
        public string Pattern;
        public Func<List<string>> Dirs;
        public long Size = -1;
    }

    static class Cleaner
    {
        public static readonly List<CleanItem> Items = new List<CleanItem>();

        static string Win { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string Roam { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        static string PD { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }

        static void Add(string id, string name, string desc, bool def, Func<List<string>> dirs, int kind = 0, string pattern = null)
        {
            Items.Add(new CleanItem { Id = id, Name = L.T(name), Desc = L.T(desc), Def = def, Dirs = dirs, Kind = kind, Pattern = pattern });
        }

        static List<string> ChromiumCaches(string userData)
        {
            var r = new List<string>();
            string ud = Path.Combine(Local, userData);
            if (!Directory.Exists(ud)) return r;
            try
            {
                foreach (var prof in Directory.GetDirectories(ud))
                {
                    string n = Path.GetFileName(prof);
                    if (n != "Default" && !n.StartsWith("Profile ")) continue;
                    r.Add(Path.Combine(prof, @"Cache\Cache_Data"));
                    r.Add(Path.Combine(prof, "Code Cache"));
                    r.Add(Path.Combine(prof, "GPUCache"));
                }
            }
            catch { }
            return r;
        }

        static Cleaner()
        {
            Add("temp", "Archivos temporales del usuario", "Restos de instaladores, actualizaciones y programas (%TEMP%).", true,
                () => new List<string> { Path.GetTempPath() });
            Add("wintemp", "Archivos temporales de Windows", "C:\\Windows\\Temp", true,
                () => new List<string> { Path.Combine(Win, "Temp") });
            Add("prefetch", "Prefetch", "Caché de arranque de programas. Windows la regenera; las apps tardan un poco más en abrir la primera vez.", true,
                () => new List<string> { Path.Combine(Win, "Prefetch") }, 0, "*.pf");
            Add("dumps", "Volcados de memoria y reportes de errores", "CrashDumps, Minidump y reportes de errores de Windows (WER).", true,
                () => new List<string> { Path.Combine(Local, "CrashDumps"), Path.Combine(PD, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(PD, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(Win, "Minidump"), Path.Combine(Win, "LiveKernelReports") });
            Add("recycle", "Papelera de reciclaje", "Vacía la papelera de todas las unidades.", true, () => new List<string>(), 1);
            Add("dns", "Caché DNS", "Resuelve problemas de conexión a servidores y fuerza direcciones actualizadas.", true, () => new List<string>(), 2);
            Add("delivery", "Caché de Optimización de distribución", "Actualizaciones de Windows ya instaladas que se comparten con otros PCs.", true,
                () => new List<string> { Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") });
            Add("wu", "Descargas de Windows Update", "Paquetes de actualización ya instalados. Seguro de borrar.", false,
                () => new List<string> { Path.Combine(Win, @"SoftwareDistribution\Download") });
            Add("browsers", "Caché de navegadores", "Chrome, Edge, Brave, Opera GX y Firefox. Cierra el navegador para borrarla toda.", false,
                () =>
                {
                    var l = new List<string>();
                    l.AddRange(ChromiumCaches(@"Google\Chrome\User Data"));
                    l.AddRange(ChromiumCaches(@"Microsoft\Edge\User Data"));
                    l.AddRange(ChromiumCaches(@"BraveSoftware\Brave-Browser\User Data"));
                    l.Add(Path.Combine(Local, @"Opera Software\Opera GX Stable\Cache"));
                    try { foreach (var p in Directory.GetDirectories(Path.Combine(Local, @"Mozilla\Firefox\Profiles"))) l.Add(Path.Combine(p, "cache2")); } catch { }
                    return l;
                });
            Add("apps", "Caché de Discord y launchers", "Discord, Steam (web) y Epic Games Launcher (web).", false,
                () =>
                {
                    var l = new List<string>();
                    foreach (var d in new[] { "discord", "discordptb", "discordcanary" })
                        foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" }) l.Add(Path.Combine(Roam, d, c));
                    l.Add(Path.Combine(Local, @"Steam\htmlcache"));
                    try { foreach (var p in Directory.GetDirectories(Path.Combine(Local, @"EpicGamesLauncher\Saved"), "webcache*")) l.Add(p); } catch { }
                    return l;
                });
            Add("shaders", "Caché de shaders (DirectX / NVIDIA / AMD)", "Solo si tienes errores gráficos o tras cambiar drivers: la primera partida tendrá tirones mientras se regenera.", false,
                () =>
                {
                    var l = new List<string> { Path.Combine(Local, "D3DSCache") };
                    foreach (var s in new[] { @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\DxcCache", @"AMD\GLCache", @"AMD\VkCache" }) l.Add(Path.Combine(Local, s));
                    return l;
                });
        }

        static long DirSize(DirectoryInfo d, int depth)
        {
            long s = 0;
            try
            {
                foreach (var f in d.EnumerateFiles()) { try { s += f.Length; } catch { } }
                if (depth < 40)
                    foreach (var c in d.EnumerateDirectories())
                        if ((c.Attributes & FileAttributes.ReparsePoint) == 0) s += DirSize(c, depth + 1);
            }
            catch { }
            return s;
        }

        public static long Measure(CleanItem it)
        {
            if (it.Kind == 2) return 0;
            if (it.Kind == 1)
            {
                var info = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                return Native.SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
            }
            long total = 0;
            foreach (var d in it.Dirs())
            {
                if (!Directory.Exists(d)) continue;
                if (it.Pattern != null) { try { foreach (var f in new DirectoryInfo(d).GetFiles(it.Pattern)) total += f.Length; } catch { } }
                else total += DirSize(new DirectoryInfo(d), 0);
            }
            return total;
        }

        static void CleanRec(DirectoryInfo d, ref long freed, bool root)
        {
            FileInfo[] fs; DirectoryInfo[] ds;
            try { fs = d.GetFiles(); ds = d.GetDirectories(); } catch { return; }
            foreach (var f in fs)
            {
                try
                {
                    long len = f.Length;
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                    f.Delete(); freed += len;
                }
                catch { }
            }
            foreach (var s in ds)
            {
                if ((s.Attributes & FileAttributes.ReparsePoint) != 0) { try { s.Delete(); } catch { } continue; }
                CleanRec(s, ref freed, false);
            }
            if (!root) { try { d.Delete(); } catch { } }
        }

        public static long Clean(CleanItem it)
        {
            if (it.Kind == 2) { Sys.Run("ipconfig", "/flushdns"); return 0; }
            if (it.Kind == 1)
            {
                long before = Measure(it);
                Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
                return before;
            }
            if (it.Id == "delivery")
            {
                long before = Measure(it);
                Sys.Run("powershell", "-NoProfile -Command \"Delete-DeliveryOptimizationCache -Force -ErrorAction SilentlyContinue\"");
                return Math.Max(0, before - Measure(it));
            }
            long freed = 0;
            foreach (var d in it.Dirs())
            {
                if (!Directory.Exists(d)) continue;
                if (it.Pattern != null)
                {
                    try { foreach (var f in new DirectoryInfo(d).GetFiles(it.Pattern)) { try { long l = f.Length; f.Delete(); freed += l; } catch { } } } catch { }
                }
                else CleanRec(new DirectoryInfo(d), ref freed, true);
            }
            return freed;
        }

        public static CleanItem Get(string id) { return Items.First(i => i.Id == id); }
    }

    // =====================================================================  SESSION HELPERS
    static class SessionOps
    {
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "System", "Idle", "csrss", "wininit", "winlogon", "lsass", "services", "smss", "svchost", "explorer", "dwm", "fontdrvhost", "GameBoost", "MsMpEng", "Registry", "audiodg", "sihost", "ctfmon" };

        public static IEnumerable<string> Lines(string s)
        {
            return (s ?? "").Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);
        }

        public static string Without(string list, string item)
        {
            return string.Join("\r\n", Lines(list).Where(x => !x.Equals(item, StringComparison.OrdinalIgnoreCase)));
        }

        public static List<string> KillApps(string list, out int count)
        {
            var paths = new List<string>(); count = 0;
            foreach (var raw in Lines(list))
            {
                string n = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw.Substring(0, raw.Length - 4) : raw;
                if (Protected.Contains(n)) continue;
                bool any = false;
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try
                    {
                        string path = null;
                        try { path = p.MainModule.FileName; } catch { }
                        p.Kill(); any = true;
                        if (path != null && !paths.Contains(path)) paths.Add(path);
                    }
                    catch { }
                }
                if (any) count++;
            }
            return paths;
        }

        public static List<string> StopServices(string list)
        {
            var stopped = new List<string>();
            foreach (var name in Lines(list))
            {
                try
                {
                    using (var sc = new ServiceController(name))
                    {
                        if (sc.Status != ServiceControllerStatus.Running || !sc.CanStop) continue;
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        stopped.Add(name);
                    }
                }
                catch { }
            }
            return stopped;
        }

        public static void StartServices(IEnumerable<string> list)
        {
            foreach (var name in list)
            {
                try { using (var sc = new ServiceController(name)) if (sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { }
            }
        }
    }

    // =====================================================================  SYSTEM INFO / HARDWARE PROFILE
    public class SysInfo
    {
        public string Cpu = "—", CpuRaw = "", CpuVendor = "", Gpu = "—", GpuVendor = "", Os = "—", Ram = "—", Display = "—", Disk = "", GpuDriver = "", FormFactor = "Sobremesa";
        public double RamGB; public int RamMHz, RamType, Hz, MaxHz, ResW, ResH, Startup;
        public bool OnBattery, HasBattery, DualX3D, IntelHybrid, DedicatedGpu, HybridGraphics, HagsSupported, IsHdd;
        public DateTime? DriverDate;

        const RegexOptions I = RegexOptions.IgnoreCase;

        public static SysInfo Collect()
        {
            var s = new SysInfo();
            try
            {
                string raw = Sys.Sz(Sys.LM, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "";
                s.CpuRaw = raw;
                s.CpuVendor = Regex.IsMatch(raw, "AMD|Ryzen", I) ? "amd" : Regex.IsMatch(raw, "Intel", I) ? "intel" : "";
                s.DualX3D = Regex.IsMatch(raw, @"(79[05]0X3D|99[05]0X3D)", I);
                s.IntelHybrid = Regex.IsMatch(raw, @"i[3579]-1[2-4]\d{2,3}|Core\(TM\) Ultra|Core Ultra", I);
                string cpu = Regex.Replace(raw, @"\((R|TM)\)|CPU|Processor|\d+-Core|with Radeon.*|@.*", "", I);
                s.Cpu = Regex.Replace(cpu, @"\s+", " ").Trim();
            }
            catch { }
            try
            {
                var gpus = new List<string[]>();
                using (var q = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
                    foreach (ManagementObject o in q.Get())
                        gpus.Add(new[] { Convert.ToString(o["Name"]), Convert.ToString(o["DriverVersion"]), Convert.ToString(o["DriverDate"]) });
                var real = gpus.Where(x => !Regex.IsMatch(x[0], "Basic|Virtual|Remote|Parsec|Meta|Mirage|Indirect", I)).ToList();
                Func<string, bool> ded = n => Regex.IsMatch(n, @"NVIDIA|GeForce|Quadro|Radeon\s*(\(TM\)\s*)?(RX|Pro|VII)|\bRX\s?\d{3,4}|Arc(\(TM\))?\s*[AB]\d", I);
                var g = real.FirstOrDefault(x => ded(x[0])) ?? real.FirstOrDefault() ?? gpus.FirstOrDefault();
                s.DedicatedGpu = real.Any(x => ded(x[0]));
                s.HybridGraphics = s.DedicatedGpu && real.Count >= 2;
                if (g != null)
                {
                    string name = g[0];
                    s.Gpu = name.Replace("(R)", "").Replace("(TM)", "").Trim();
                    s.GpuVendor = Regex.IsMatch(name, "NVIDIA|GeForce|Quadro", I) ? "nvidia" : Regex.IsMatch(name, "AMD|Radeon", I) ? "amd" : Regex.IsMatch(name, "Intel", I) ? "intel" : "";
                    s.HagsSupported = s.DedicatedGpu && Regex.IsMatch(name, @"GTX\s*1[06]\d{2}|RTX|RX\s*[5-9]\d{3}|Arc", I);
                    string ver = g[1] ?? "";
                    if (s.GpuVendor == "nvidia")
                    {
                        string digits = ver.Replace(".", "");
                        if (digits.Length >= 5) { string d5 = digits.Substring(digits.Length - 5); s.GpuDriver = d5.Substring(0, 3) + "." + d5.Substring(3); }
                    }
                    else s.GpuDriver = ver;
                    try { if (!string.IsNullOrEmpty(g[2])) s.DriverDate = ManagementDateTimeConverter.ToDateTime(g[2]); } catch { }
                }
            }
            catch { }
            try
            {
                ulong cap = 0;
                using (var q = new ManagementObjectSearcher("SELECT Capacity, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject o in q.Get())
                    {
                        cap += Convert.ToUInt64(o["Capacity"] ?? 0);
                        if (o["ConfiguredClockSpeed"] != null) s.RamMHz = Math.Max(s.RamMHz, Convert.ToInt32(o["ConfiguredClockSpeed"]));
                        if (o["SMBIOSMemoryType"] != null) s.RamType = Convert.ToInt32(o["SMBIOSMemoryType"]);
                    }
                if (cap == 0) { var m = new Native.MEMORYSTATUSEX(); Native.GlobalMemoryStatusEx(m); cap = m.ullTotalPhys; }
                s.RamGB = Math.Round(cap / 1073741824.0);
                string type = s.RamType == 34 ? "DDR5" : s.RamType == 26 ? "DDR4" : s.RamType == 24 ? "DDR3" : "";
                s.Ram = s.RamGB + " GB" + (type != "" ? " · " + type : "") + (s.RamMHz > 0 ? " " + s.RamMHz + " MT/s" : "");
            }
            catch { }
            try
            {
                string cv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
                int build; int.TryParse(Sys.Sz(Sys.LM, cv, "CurrentBuild") ?? "0", out build);
                string ed = Sys.Sz(Sys.LM, cv, "EditionID") ?? "";
                ed = ed == "Professional" ? "Pro" : ed == "Core" ? "Home" : ed;
                s.Os = (build >= 22000 ? "Windows 11 " : "Windows 10 ") + ed + " " + (Sys.Sz(Sys.LM, cv, "DisplayVersion") ?? "");
            }
            catch { }
            try
            {
                var dm = new Native.DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
                if (Native.EnumDisplaySettings(null, -1, ref dm))
                {
                    s.Hz = dm.dmDisplayFrequency; s.ResW = dm.dmPelsWidth; s.ResH = dm.dmPelsHeight; s.MaxHz = s.Hz;
                    var m = new Native.DEVMODE(); m.dmSize = dm.dmSize;
                    for (int i = 0; Native.EnumDisplaySettings(null, i, ref m) && i < 2000; i++)
                        if (m.dmPelsWidth == s.ResW && m.dmPelsHeight == s.ResH && m.dmDisplayFrequency > s.MaxHz) s.MaxHz = m.dmDisplayFrequency;
                    s.Display = s.ResW + "×" + s.ResH + " · " + s.Hz + " Hz";
                }
            }
            catch { }
            try
            {
                Native.SYSTEM_POWER_STATUS ps;
                if (Native.GetSystemPowerStatus(out ps)) { s.HasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255; s.OnBattery = s.HasBattery && ps.ACLineStatus == 0; }
                s.FormFactor = s.HasBattery ? "Portátil" : "Sobremesa";
            }
            catch { }
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                char sysLetter = char.ToUpperInvariant(Environment.SystemDirectory[0]);
                int diskNum = -1;
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition")))
                    foreach (ManagementObject o in q.Get())
                    {
                        var dl = o["DriveLetter"];
                        if (dl != null && char.ToUpperInvariant(Convert.ToChar(dl)) == sysLetter) diskNum = Convert.ToInt32(o["DiskNumber"]);
                    }
                int mt = 0, bt = 0;
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk")))
                    foreach (ManagementObject o in q.Get())
                        if (Convert.ToString(o["DeviceId"]) == diskNum.ToString()) { mt = Convert.ToInt32(o["MediaType"]); bt = Convert.ToInt32(o["BusType"]); }
                s.IsHdd = mt == 3;
                s.Disk = bt == 17 ? "SSD NVMe" : mt == 4 ? "SSD" : mt == 3 ? "HDD" : "";
            }
            catch { }
            s.Startup = CountStartup();
            return s;
        }

        static int CountStartup()
        {
            int n = 0;
            string appr = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
            Func<RegistryKey, string, string, int> count = (root, run, approved) =>
            {
                int c = 0;
                try
                {
                    using (var k = root.OpenSubKey(run))
                    {
                        if (k == null) return 0;
                        foreach (var v in k.GetValueNames())
                        {
                            byte[] b = null;
                            try { using (var a = root.OpenSubKey(appr + approved)) if (a != null) b = a.GetValue(v) as byte[]; } catch { }
                            if (b == null || b.Length == 0 || (b[0] & 1) == 0) c++;
                        }
                    }
                }
                catch { }
                return c;
            };
            n += count(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run");
            n += count(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run");
            n += count(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32");
            try { n += Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Startup)).Count(f => !f.EndsWith(".ini")); } catch { }
            return n;
        }
    }

    // =====================================================================  GAME DETECTION
    static class Detect
    {
        static readonly string[] BadExe = { "unins", "crash", "setup", "redist", "vc_", "dxsetup", "prereq", "easyanticheat", "battleye", "beservice", "report", "helper", "cef", "update", "install", "dotnet", "launcher", "overlay", "anticheat", "eossdk", "quicksfv", "cleanup" };

        public static string GuessExe(string dir)
        {
            string best = null; long bestLen = 0;
            Walk(dir, 0, ref best, ref bestLen);
            return best;
        }
        static void Walk(string dir, int depth, ref string best, ref long bestLen)
        {
            if (depth > 3 || !Directory.Exists(dir)) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.exe"))
                {
                    string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (BadExe.Any(b => n.Contains(b))) continue;
                    long len = new FileInfo(f).Length;
                    if (len > bestLen) { bestLen = len; best = f; }
                }
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(d).ToLowerInvariant();
                    if (n == "_commonredist" || n == "redist" || n == "redistributables" || n == "directx" || (n == "engine" && depth > 0)) continue;
                    Walk(d, depth + 1, ref best, ref bestLen);
                }
            }
            catch { }
        }

        public static List<Game> All()
        {
            var l = new List<Game>();
            try { l.AddRange(Steam()); } catch { }
            try { l.AddRange(Epic()); } catch { }
            try { l.AddRange(Riot()); } catch { }
            return l;
        }

        static List<Game> Steam()
        {
            var res = new List<Game>();
            string sp = Sys.Sz(Sys.CU, @"Software\Valve\Steam", "SteamPath");
            if (string.IsNullOrEmpty(sp)) return res;
            sp = Path.GetFullPath(sp);
            var libs = new List<string> { sp };
            string vdf = Path.Combine(sp, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (!libs.Any(x => string.Equals(Path.GetFullPath(x), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase))) libs.Add(p);
                }
            var skip = new HashSet<string> { "228980", "1070560", "1391110", "1628350", "250820", "1493710", "1826330", "2180100", "993090", "431960" };
            foreach (var lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string t = File.ReadAllText(acf);
                    string id = Val(t, "appid"), name = Val(t, "name"), dir = Val(t, "installdir");
                    if (id == null || name == null || skip.Contains(id)) continue;
                    if (Regex.IsMatch(name, "Redistributable|Proton|Steamworks|Steam Linux|SteamVR|Dedicated Server|Soundtrack|SDK", RegexOptions.IgnoreCase)) continue;
                    string full = Path.Combine(apps, "common", dir ?? "");
                    string exe = GuessExe(full);
                    string img = null;
                    foreach (var c in new[] { Path.Combine(sp, @"appcache\librarycache\" + id + "_header.jpg"), Path.Combine(sp, @"appcache\librarycache\" + id + @"\header.jpg") })
                        if (File.Exists(c)) { img = c; break; }
                    if (img == null)
                    {
                        string d = Path.Combine(sp, @"appcache\librarycache\" + id);
                        try { if (Directory.Exists(d)) img = Directory.GetFiles(d, "header*.jpg", SearchOption.AllDirectories).FirstOrDefault(); } catch { }
                    }
                    res.Add(new Game
                    {
                        Id = "steam:" + id, Name = name, Source = "Steam", Target = "steam://rungameid/" + id, Args = "",
                        Proc = exe != null ? Path.GetFileNameWithoutExtension(exe) : "", Priority = "High", Image = img, Icon = exe
                    });
                }
            }
            return res;
        }
        static string Val(string t, string key)
        {
            var m = Regex.Match(t, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        static List<Game> Epic()
        {
            var res = new List<Game>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            var js = new JavaScriptSerializer();
            foreach (var f in Directory.Exists(dir) ? Directory.GetFiles(dir, "*.item") : new string[0])
            {
                try
                {
                    var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    Func<string, string> S = k => d.ContainsKey(k) && d[k] != null ? d[k].ToString() : "";
                    var cats = d.ContainsKey("AppCategories") ? d["AppCategories"] as ArrayList : null;
                    if (cats != null && !cats.Cast<object>().Any(c => c.ToString() == "games")) continue;
                    if (S("bIsIncompleteInstall") == "True") continue;
                    string main = S("MainGameAppName"), app = S("AppName");
                    if (main != "" && main != app) continue;
                    string loc = S("InstallLocation"), exeRel = S("LaunchExecutable");
                    string exe = exeRel != "" ? Path.Combine(loc, exeRel) : null;
                    string proc = exe != null ? Path.GetFileNameWithoutExtension(exe) : "";
                    if (app == "Fortnite")
                    {
                        proc = "FortniteClient-Win64-Shipping";
                        string ship = Path.Combine(loc, @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe");
                        if (File.Exists(ship)) exe = ship;
                    }
                    else if (exe != null && Regex.IsMatch(proc, "launcher", RegexOptions.IgnoreCase))
                    {
                        string g = GuessExe(loc); if (g != null) proc = Path.GetFileNameWithoutExtension(g);
                    }
                    res.Add(new Game
                    {
                        Id = "epic:" + app, Name = S("DisplayName"), Source = "Epic",
                        Target = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(S("CatalogNamespace") + ":" + S("CatalogItemId") + ":" + app) + "?action=launch&silent=true",
                        Args = "", Proc = proc, Priority = "High", Icon = exe != null && File.Exists(exe) ? exe : null
                    });
                }
                catch { }
            }
            // Respaldo: carpetas de juegos de Epic aún sin manifiesto (instalación en curso, launcher reinstalado...)
            var roots = new List<string>();
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                roots.Add(Path.Combine(pf, "Epic Games"));
            foreach (var drv in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                roots.Add(Path.Combine(drv.RootDirectory.FullName, "Epic Games"));
            var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Launcher", "DirectXRedist", "GameInputRedist", "Epic Online Services", "Epic Games Launcher" };
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                foreach (var gd in Directory.GetDirectories(root))
                {
                    string n = Path.GetFileName(gd);
                    if (skipDirs.Contains(n) || n.StartsWith(".")) continue;
                    bool isFn = Directory.Exists(Path.Combine(gd, "FortniteGame"));
                    string id = "epic:" + (isFn ? "Fortnite" : n);
                    if (res.Any(x => x.Id == id || (x.Name ?? "").Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                    if (isFn)
                    {
                        string ship = Path.Combine(gd, @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe");
                        res.Add(new Game
                        {
                            Id = id, Name = "Fortnite", Source = "Epic",
                            Target = "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true",
                            Args = "", Proc = "FortniteClient-Win64-Shipping", Priority = "High", Icon = File.Exists(ship) ? ship : null
                        });
                        continue;
                    }
                    string exe = GuessExe(gd);
                    if (exe == null) continue;
                    res.Add(new Game { Id = id, Name = n, Source = "Epic", Target = exe, Args = "", Proc = Path.GetFileNameWithoutExtension(exe), Priority = "High", Icon = exe });
                }
            }
            return res;
        }

        static List<Game> Riot()
        {
            var res = new List<Game>();
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string rc = null;
            try
            {
                string j = Path.Combine(pd, @"Riot Games\RiotClientInstalls.json");
                if (File.Exists(j))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(j));
                    if (d.ContainsKey("rc_default")) rc = Convert.ToString(d["rc_default"]);
                }
            }
            catch { }
            if (rc == null || !File.Exists(rc)) rc = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            if (!File.Exists(rc)) return res;
            Func<string, string> install = prod =>
            {
                try
                {
                    string y = Path.Combine(pd, @"Riot Games\Metadata\" + prod + @"\" + prod + ".product_settings.yaml");
                    if (!File.Exists(y)) return null;
                    var m = Regex.Match(File.ReadAllText(y), "product_install_full_path:\\s*\"?([^\"\\r\\n]+)");
                    return m.Success ? m.Groups[1].Value.Replace('/', '\\') : "";
                }
                catch { return null; }
            };
            string val = install("valorant.live");
            if (val != null)
            {
                string ico = Path.Combine(val, "VALORANT.exe");
                res.Add(new Game { Id = "riot:valorant", Name = "VALORANT", Source = "Riot", Target = rc, Args = "--launch-product=valorant --launch-patchline=live", Proc = "VALORANT-Win64-Shipping", Priority = "High", Icon = File.Exists(ico) ? ico : rc });
            }
            string lol = install("league_of_legends.live");
            if (lol != null)
            {
                string ico = Path.Combine(lol, @"Game\League of Legends.exe");
                res.Add(new Game { Id = "riot:lol", Name = "League of Legends", Source = "Riot", Target = rc, Args = "--launch-product=league_of_legends --launch-patchline=live", Proc = "League of Legends", Priority = "High", Icon = File.Exists(ico) ? ico : rc });
            }
            return res;
        }
    }
}
