// Macros de GameBoost (BLAD Studios)
//
// Un atajo global ejecuta una lista de pasos: escribir texto, pulsar una combinación de teclas,
// esperar o abrir un programa. Pensado para apps como Word o el correo, NO para juegos:
// las macros se pausan con el Modo Juego activo o con un juego en primer plano.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GameBoost
{
    public class MacroStep
    {
        public string Kind = "text"; // text | keys | wait | open
        public string Value = "";
    }

    public class Macro
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "", Hotkey = "";
        public bool Enabled = true;
        public List<MacroStep> Steps = new List<MacroStep>();
    }

    public class MacroList { public List<Macro> Items = new List<Macro>(); }

    // Texto de atajos: "Ctrl+Alt+D", "Ctrl+Shift+F5", "Enter"... (la última parte es el nombre de la tecla en WPF; los dígitos se escriben tal cual).
    static class HotkeyText
    {
        public static string FromKeys(ModifierKeys m, Key k)
        {
            if (k == Key.None || k == Key.System || k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt ||
                k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return null;
            var parts = new List<string>();
            if ((m & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((m & ModifierKeys.Windows) != 0) parts.Add("Win");
            string n = k.ToString();
            if (n.Length == 2 && n[0] == 'D' && char.IsDigit(n[1])) n = n.Substring(1);
            parts.Add(n);
            return string.Join("+", parts);
        }

        // Un atajo global sin Ctrl/Alt/Win robaría esa tecla a todas las apps; se exige un modificador o una tecla F.
        public static bool IsSafeHotkey(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split('+');
            if (parts.Any(p => p == "Ctrl" || p == "Alt" || p == "Win")) return true;
            string last = parts[parts.Length - 1];
            int n;
            return last.Length >= 2 && last[0] == 'F' && int.TryParse(last.Substring(1), out n) && n >= 1 && n <= 24;
        }

        // mods en formato RegisterHotKey: Alt=1, Ctrl=2, Shift=4, Win=8.
        public static bool Parse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var raw in s.Split('+'))
            {
                string p = raw.Trim();
                if (p == "Ctrl") mods |= 2;
                else if (p == "Alt") mods |= 1;
                else if (p == "Shift") mods |= 4;
                else if (p == "Win") mods |= 8;
                else
                {
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    Key k;
                    if (Enum.TryParse(p, true, out k) && k != Key.None) vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                }
            }
            return vk != 0;
        }
    }

    class MacroHost
    {
        const int BaseId = 0x5100;
        public readonly MacroList Data;
        readonly Func<string> blockReason;
        readonly Dictionary<int, Macro> ids = new Dictionary<int, Macro>();
        HwndSource hk;
        bool paused;
        volatile bool running;

        // Mensaje para el usuario (texto, tipo de aviso). Puede llegar desde otro hilo.
        public event Action<string, int> Notify;

        public MacroHost(MacroList data, bool live, Func<string> blockReason)
        {
            Data = data;
            if (Data.Items == null) Data.Items = new List<Macro>();
            this.blockReason = blockReason;
            if (!live) return;
            hk = new HwndSource(new HwndSourceParameters("GameBoostMacros") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            hk.AddHook(Hook);
        }

        // Registra los atajos de todas las macros activas. Devuelve los nombres de las que no se pudieron registrar.
        public List<string> Register()
        {
            var failed = new List<string>();
            Unregister();
            if (hk == null || paused) return failed;
            int i = 0;
            foreach (var m in Data.Items)
            {
                int id = BaseId + i++;
                uint mods, vk;
                if (!m.Enabled || m.Steps == null || m.Steps.Count == 0 || !HotkeyText.Parse(m.Hotkey, out mods, out vk)) continue;
                if (ChNative.RegisterHotKey(hk.Handle, id, mods | 0x4000, vk)) ids[id] = m;
                else failed.Add(string.IsNullOrEmpty(m.Name) ? m.Hotkey : m.Name);
            }
            return failed;
        }

        void Unregister()
        {
            if (hk != null) foreach (var id in ids.Keys) ChNative.UnregisterHotKey(hk.Handle, id);
            ids.Clear();
        }

        // Durante el Modo Juego se liberan los atajos para que el juego reciba esas teclas con normalidad.
        public void SetPaused(bool p)
        {
            if (paused == p) return;
            paused = p;
            if (p) Unregister(); else Register();
        }

        public void Save() { Store.Save("macros.json", Data); }

        public void Dispose()
        {
            Unregister();
            if (hk != null) { hk.Dispose(); hk = null; }
        }

        void Say(string s, int kind) { if (Notify != null) Notify(s, kind); }

        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg != 0x0312) return IntPtr.Zero;
            Macro m;
            if (!ids.TryGetValue(w.ToInt32(), out m)) return IntPtr.Zero;
            handled = true;
            string why = blockReason != null ? blockReason() : null;
            if (why != null) { Say(L.F("Macro en pausa: {0}", why), 3); return IntPtr.Zero; }
            if (running) return IntPtr.Zero;
            string clip = "";
            try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
            var steps = m.Steps.Select(s => new MacroStep { Kind = s.Kind, Value = s.Value }).ToList();
            running = true;
            new Thread(() =>
            {
                try { Run(steps, clip); }
                catch (Exception ex) { Say(L.F("La macro falló: {0}", ex.Message), 3); }
                finally { running = false; }
            }) { IsBackground = true }.Start();
            return IntPtr.Zero;
        }

        static void Run(List<MacroStep> steps, string clip)
        {
            WaitModifiersUp();
            foreach (var s in steps)
            {
                string v = s.Value ?? "";
                uint mods, vk;
                int ms;
                switch (s.Kind)
                {
                    case "text":
                        TypeText(v.Replace("{fecha}", DateTime.Now.ToString("d")).Replace("{hora}", DateTime.Now.ToString("t")).Replace("{portapapeles}", clip));
                        break;
                    case "keys":
                        if (HotkeyText.Parse(v, out mods, out vk)) SendCombo(mods, vk);
                        break;
                    case "wait":
                        if (int.TryParse(v.Trim(), out ms)) Thread.Sleep(Math.Max(0, Math.Min(60000, ms)));
                        break;
                    case "open":
                        if (v.Trim().Length > 0) Process.Start(new ProcessStartInfo(v.Trim()) { UseShellExecute = true });
                        break;
                }
                Thread.Sleep(30);
            }
        }

        public static string ForegroundProcess()
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                return pid == 0 ? null : Process.GetProcessById((int)pid).ProcessName;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ envío de teclas
        // Espera a que el usuario suelte Ctrl/Alt/Shift/Win del atajo; si no, el texto saldría combinado con ellas.
        static void WaitModifiersUp()
        {
            var sw = Stopwatch.StartNew();
            int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C };
            while (sw.ElapsedMilliseconds < 3000 && keys.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) Thread.Sleep(15);
        }

        static void TypeText(string s)
        {
            var list = new List<INPUT>();
            foreach (char ch in s)
            {
                if (ch == '\r') continue;
                if (ch == '\n') { list.Add(Key(0x0D, false)); list.Add(Key(0x0D, true)); continue; }
                if (ch == '\t') { list.Add(Key(0x09, false)); list.Add(Key(0x09, true)); continue; }
                list.Add(Uni(ch, false)); list.Add(Uni(ch, true));
            }
            for (int i = 0; i < list.Count; i += 40)
            {
                Send(list.Skip(i).Take(40).ToArray());
                Thread.Sleep(5);
            }
        }

        static void SendCombo(uint mods, uint vk)
        {
            var mvk = new List<ushort>();
            if ((mods & 2) != 0) mvk.Add(0x11);
            if ((mods & 1) != 0) mvk.Add(0x12);
            if ((mods & 4) != 0) mvk.Add(0x10);
            if ((mods & 8) != 0) mvk.Add(0x5B);
            var list = new List<INPUT>();
            foreach (var m in mvk) list.Add(Key(m, false));
            list.Add(Key((ushort)vk, false));
            list.Add(Key((ushort)vk, true));
            for (int i = mvk.Count - 1; i >= 0; i--) list.Add(Key(mvk[i], true));
            Send(list.ToArray());
        }

        static bool Extended(ushort vk)
        {
            return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C || vk == 0x6F || vk == 0x90;
        }

        static INPUT Key(ushort vk, bool up)
        {
            var i = new INPUT { type = 1 };
            i.U.ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = (up ? 2u : 0u) | (Extended(vk) ? 1u : 0u) };
            return i;
        }

        static INPUT Uni(char ch, bool up)
        {
            var i = new INPUT { type = 1 };
            i.U.ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = 4u | (up ? 2u : 0u) };
            return i;
        }

        static void Send(INPUT[] inputs)
        {
            if (inputs.Length > 0) SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    }
}
