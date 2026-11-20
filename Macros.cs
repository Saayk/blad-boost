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
        // Tiempos en ms (0 = lo más rápido posible).
        // Texto: Hold = cuánto se mantiene cada letra, Delay = pausa entre letras.
        // Teclas: Hold = cuánto se mantiene la combinación, Delay = pausa al terminar el paso.
        public int Hold, Delay;
    }

    public class Macro
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "", Hotkey = "";
        public bool Enabled = true;
        public int StartDelay; // ms antes de empezar
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

        // Botones del ratón que pueden ser atajo: rueda (Mouse3) y laterales (Mouse4, Mouse5).
        // El clic izquierdo y el derecho no se permiten: bloquearían el ratón en todo Windows.
        public static string FromMouse(ModifierKeys m, MouseButton b)
        {
            string n = b == MouseButton.Middle ? "Mouse3" : b == MouseButton.XButton1 ? "Mouse4" : b == MouseButton.XButton2 ? "Mouse5" : null;
            if (n == null) return null;
            string k = FromKeys(m, Key.A);
            return k.Substring(0, k.Length - 1) + n;
        }

        // button: 3 = rueda, 4 = lateral trasero, 5 = lateral delantero.
        public static bool ParseMouse(string s, out uint mods, out int button)
        {
            mods = 0; button = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var raw in s.Split('+'))
            {
                string p = raw.Trim();
                if (p == "Ctrl") mods |= 2;
                else if (p == "Alt") mods |= 1;
                else if (p == "Shift") mods |= 4;
                else if (p == "Win") mods |= 8;
                else if (p == "Mouse3") button = 3;
                else if (p == "Mouse4") button = 4;
                else if (p == "Mouse5") button = 5;
                else return false;
            }
            return button != 0;
        }

        // Sin Ctrl/Alt/Win (ni tecla F ni botón del ratón) el atajo le quita esa tecla a todas las apps: se avisa, pero se permite.
        public static bool IsSafeHotkey(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split('+');
            if (parts.Any(p => p == "Ctrl" || p == "Alt" || p == "Win" || p.StartsWith("Mouse"))) return true;
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

        // Atajos con botones del ratón: se detectan con un gancho de ratón de bajo nivel (RegisterHotKey no admite ratón).
        class MouseBind { public uint Mods; public int Button; public Macro M; }
        readonly List<MouseBind> mouseBinds = new List<MouseBind>();
        readonly bool[] swallowUp = new bool[6];
        LowLevelProc mouseProc;
        IntPtr mouseHook = IntPtr.Zero;

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
                if (!m.Enabled || m.Steps == null || m.Steps.Count == 0) continue;
                string label = string.IsNullOrEmpty(m.Name) ? m.Hotkey : m.Name;
                uint mods, vk;
                int button;
                if (HotkeyText.ParseMouse(m.Hotkey, out mods, out button))
                {
                    if (mouseBinds.Any(b => b.Mods == mods && b.Button == button)) failed.Add(label);
                    else mouseBinds.Add(new MouseBind { Mods = mods, Button = button, M = m });
                    continue;
                }
                if (!HotkeyText.Parse(m.Hotkey, out mods, out vk)) continue;
                if (ChNative.RegisterHotKey(hk.Handle, id, mods | 0x4000, vk)) ids[id] = m;
                else failed.Add(label);
            }
            if (mouseBinds.Count > 0)
            {
                mouseProc = MouseHookProc;
                mouseHook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, mouseProc, GetModuleHandle(null), 0);
                if (mouseHook == IntPtr.Zero) { failed.AddRange(mouseBinds.Select(b => string.IsNullOrEmpty(b.M.Name) ? b.M.Hotkey : b.M.Name)); mouseBinds.Clear(); }
            }
            return failed;
        }

        public bool MouseHookActive { get { return mouseHook != IntPtr.Zero; } }

        void Unregister()
        {
            if (hk != null) foreach (var id in ids.Keys) ChNative.UnregisterHotKey(hk.Handle, id);
            ids.Clear();
            if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            mouseBinds.Clear();
            for (int b = 0; b < swallowUp.Length; b++) swallowUp[b] = false;
        }

        // Se ejecuta en el hilo de la interfaz con cada evento del ratón: tiene que ser muy rápido.
        IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = wParam.ToInt32();
                var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool injected = (info.flags & 1) != 0;
                int button = 0; bool down = false;
                if (msg == 0x207 || msg == 0x208) { button = 3; down = msg == 0x207; }
                else if (msg == 0x20B || msg == 0x20C) { button = ((info.mouseData >> 16) & 0xFFFF) == 1 ? 4 : 5; down = msg == 0x20B; }
                if (button != 0 && !injected)
                {
                    if (!down && swallowUp[button]) { swallowUp[button] = false; return new IntPtr(1); }
                    if (down)
                    {
                        uint mods = CurrentMods();
                        var bind = mouseBinds.FirstOrDefault(b => b.Button == button && b.Mods == mods);
                        // Con un juego delante el botón pasa al juego tal cual (no se bloquea ni se avisa).
                        if (bind != null && (blockReason == null || blockReason() == null))
                        {
                            swallowUp[button] = true;
                            var m = bind.M;
                            if (hk != null) hk.Dispatcher.BeginInvoke(new Action(() => Trigger(m)));
                            return new IntPtr(1);
                        }
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        static uint CurrentMods()
        {
            uint m = 0;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) m |= 2;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) m |= 1;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) m |= 4;
            if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0) m |= 8;
            return m;
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
            Trigger(m);
            return IntPtr.Zero;
        }

        void Trigger(Macro m)
        {
            if (running) return;
            string clip = "";
            try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
            var steps = m.Steps.Select(s => new MacroStep { Kind = s.Kind, Value = s.Value, Hold = s.Hold, Delay = s.Delay }).ToList();
            int start = Clamp(m.StartDelay, 10000);
            running = true;
            new Thread(() =>
            {
                try { Run(steps, clip, start); }
                catch (Exception ex) { Say(L.F("La macro falló: {0}", ex.Message), 3); }
                finally { running = false; }
            }) { IsBackground = true }.Start();
        }

        static int Clamp(int v, int max) { return Math.Max(0, Math.Min(max, v)); }

        static void Run(List<MacroStep> steps, string clip, int startDelay)
        {
            WaitModifiersUp();
            if (startDelay > 0) Thread.Sleep(startDelay);
            foreach (var s in steps)
            {
                string v = s.Value ?? "";
                int hold = Clamp(s.Hold, 5000), delay = Clamp(s.Delay, 10000);
                uint mods, vk;
                int ms;
                switch (s.Kind)
                {
                    case "text":
                        TypeText(v.Replace("{fecha}", DateTime.Now.ToString("d")).Replace("{hora}", DateTime.Now.ToString("t")).Replace("{portapapeles}", clip), hold, delay);
                        break;
                    case "keys":
                        if (HotkeyText.Parse(v, out mods, out vk)) SendCombo(mods, vk, hold);
                        if (delay > 0) Thread.Sleep(delay);
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

        static void TypeText(string s, int hold, int delay)
        {
            // Sin tiempos: se envía en bloques (lo más rápido). Con tiempos: letra a letra.
            if (hold == 0 && delay == 0)
            {
                var list = new List<INPUT>();
                foreach (char ch in s)
                {
                    if (ch == '\r') continue;
                    list.Add(Letter(ch, false)); list.Add(Letter(ch, true));
                }
                for (int i = 0; i < list.Count; i += 40)
                {
                    Send(list.Skip(i).Take(40).ToArray());
                    Thread.Sleep(5);
                }
                return;
            }
            bool first = true;
            foreach (char ch in s)
            {
                if (ch == '\r') continue;
                if (!first && delay > 0) Thread.Sleep(delay);
                first = false;
                Send(new[] { Letter(ch, false) });
                if (hold > 0) Thread.Sleep(hold);
                Send(new[] { Letter(ch, true) });
            }
        }

        // Salto de línea y tabulador se envían como teclas reales; el resto como carácter Unicode.
        static INPUT Letter(char ch, bool up)
        {
            if (ch == '\n') return Key(0x0D, up);
            if (ch == '\t') return Key(0x09, up);
            return Uni(ch, up);
        }

        static void SendCombo(uint mods, uint vk, int hold)
        {
            var mvk = new List<ushort>();
            if ((mods & 2) != 0) mvk.Add(0x11);
            if ((mods & 1) != 0) mvk.Add(0x12);
            if ((mods & 4) != 0) mvk.Add(0x10);
            if ((mods & 8) != 0) mvk.Add(0x5B);
            var down = new List<INPUT>();
            foreach (var m in mvk) down.Add(Key(m, false));
            down.Add(Key((ushort)vk, false));
            var up = new List<INPUT> { Key((ushort)vk, true) };
            for (int i = mvk.Count - 1; i >= 0; i--) up.Add(Key(mvk[i], true));
            Send(down.ToArray());
            if (hold > 0) Thread.Sleep(hold);
            Send(up.ToArray());
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

        delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    }
}
