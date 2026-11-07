using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GameBoost
{
    // Traducción: el texto en español es la clave; si falta una traducción se muestra el español.
    static class L
    {
        public static bool En;
        public static readonly HashSet<string> Missing = new HashSet<string>();

        public static void Init(string lang)
        {
            if (lang == "en") En = true;
            else if (lang == "es") En = false;
            else En = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "es";
        }

        public static string T(string es)
        {
            if (!En || string.IsNullOrEmpty(es) || !es.Any(char.IsLetter)) return es;
            string v;
            if (D.TryGetValue(es, out v)) return v;
            lock (Missing) Missing.Add(es);
            return es;
        }

        public static string F(string es, params object[] a) { return string.Format(T(es), a); }

        public static void Translate(DependencyObject o)
        {
            if (!En || o == null) return;
            var w = o as Window; if (w != null) w.Title = T(w.Title);
            var tb = o as TextBlock; if (tb != null && !string.IsNullOrEmpty(tb.Text)) tb.Text = T(tb.Text);
            var cc = o as ContentControl; if (cc != null && cc.Content is string) cc.Content = T((string)cc.Content);
            foreach (object child in LogicalTreeHelper.GetChildren(o)) { var d = child as DependencyObject; if (d != null) Translate(d); }
        }

        static readonly Dictionary<string, string> D = new Dictionary<string, string>
        {
            // ---------------- marca / nombres propios
            { "GameBoost", "GameBoost" }, { "GameBoost · BLAD Studios", "GameBoost · BLAD Studios" }, { "by BLAD Studios", "by BLAD Studios" },
            { "© 2026 BLAD Studios", "© 2026 BLAD Studios" }, { "Benchmark", "Benchmark" }, { "Ping", "Ping" }, { "Jitter", "Jitter" },
            { "Normal", "Normal" }, { "Prefetch", "Prefetch" }, { "C:\\Windows\\Temp", "C:\\Windows\\Temp" }, { "GENERAL", "GENERAL" },
            { "AMD Anti-Lag 2", "AMD Anti-Lag 2" }, { "GameBoost Setup", "GameBoost Setup" },

            // ---------------- navegación y estructura (XAML)
            { "Modo Juego inactivo", "Game Mode off" }, { "Pulsa Jugar en cualquier juego", "Press Play on any game" },
            { "Panel", "Dashboard" }, { "Juegos", "Games" }, { "Optimizar", "Optimize" }, { "Limpieza", "Cleanup" },
            { "Consejos", "Tips" }, { "Apps recomendadas", "Recommended apps" }, { "SISTEMA", "SYSTEM" }, { "Ajustes", "Settings" },
            { "Omitir", "Skip" }, { "Actualizar ahora", "Update now" }, { "Analizando tu equipo...", "Analyzing your PC..." },
            { "Activar Modo Juego", "Enable Game Mode" }, { "optimizado", "optimized" }, { "Analizando...", "Analyzing..." },
            { "Aplicar recomendados", "Apply recommended" }, { "Acceso rápido", "Quick launch" }, { "Ver biblioteca", "View library" },
            { "Recomendaciones", "Recommendations" }, { "Tu equipo", "Your PC" }, { "Detectando juegos instalados...", "Detecting installed games..." },
            { "Buscar juegos", "Scan for games" }, { "Agregar juego", "Add game" },
            { "Ajustes de Windows para más FPS y menos latencia. Todo es reversible.", "Windows tweaks for more FPS and lower latency. Everything is reversible." },
            { "Restaurar Windows", "Restore Windows" }, { "Perfil adaptado a tu PC", "Profile tailored to your PC" },
            { "Elimina archivos basura de forma segura. Los archivos en uso se omiten.", "Safely removes junk files. Files in use are skipped." },
            { "Analizar", "Analyze" }, { "Limpiar seleccionado", "Clean selected" },
            { "Pulsa Analizar para calcular el espacio recuperable", "Press Analyze to calculate recoverable space" },
            { "Mide tu PC antes y después de optimizar. Compara siempre en las mismas condiciones.", "Measure your PC before and after optimizing. Always compare under the same conditions." },
            { "Usar como referencia", "Set as baseline" }, { "Ejecutar benchmark", "Run benchmark" }, { "PUNTUACIÓN GAMEBOOST", "GAMEBOOST SCORE" },
            { "Aún no hay resultados. Ejecuta el primer benchmark (tarda unos 15 segundos).", "No results yet. Run your first benchmark (it takes about 15 seconds)." },
            { "HISTORIAL", "HISTORY" },
            { "El benchmark mide lo que GameBoost optimiza: CPU, precisión del temporizador, carga en segundo plano, memoria y red. Para medir los FPS reales dentro de un juego usa MSI Afterburner + RivaTuner (Apps recomendadas).", "The benchmark measures what GameBoost optimizes: CPU, timer precision, background load, memory and network. To measure real in-game FPS, use MSI Afterburner + RivaTuner (Recommended apps)." },
            { "Lo que Windows no puede ajustar solo: configuración del juego, drivers y hardware.", "What Windows can't tune on its own: game settings, drivers and hardware." },
            { "Software de confianza que complementa a GameBoost. Las marcadas con 'Para tu PC' coinciden con tu hardware.", "Trusted software that complements GameBoost. Apps marked 'For your PC' match your hardware." },
            { "Define qué hace GameBoost cuando pulsas Jugar o activas el Modo Juego.", "Choose what GameBoost does when you press Play or enable Game Mode." },
            { "MODO JUEGO", "GAME MODE" }, { "Apps que se cierran al jugar", "Apps closed while gaming" }, { "Un proceso por línea (sin .exe)", "One process per line (without .exe)" },
            { "Servicios que se pausan", "Paused services" }, { "Se detienen al jugar y se reanudan al terminar", "Stopped while you play and resumed afterwards" },
            { "COPIA DE SEGURIDAD", "BACKUP" }, { "Tu configuración original", "Your original settings" }, { "Restaurar mi configuración original", "Restore my original settings" },
            { "HERRAMIENTAS", "TOOLS" }, { "Crear punto de restauración", "Create restore point" }, { "Restaurar todos los ajustes de Windows", "Reset all tweaks to Windows defaults" },
            { "Buscar actualizaciones", "Check for updates" }, { "Abrir carpeta de datos", "Open data folder" }, { "ACERCA DE", "ABOUT" },
            { "Desarrollado por BLAD Studios", "Developed by BLAD Studios" }, { "ACTIVIDAD", "ACTIVITY" }, { "Editar juego", "Edit game" },
            { "Nombre", "Name" }, { "Ejecutable o URL de lanzamiento", "Executable or launch URL" }, { "Argumentos (opcional)", "Arguments (optional)" },
            { "Proceso del juego (sin .exe)", "Game process (without .exe)" }, { "Prioridad de CPU", "CPU priority" }, { "Superior", "Above normal" },
            { "Alta (recomendado)", "High (recommended)" }, { "Eliminar juego", "Delete game" }, { "Cancelar", "Cancel" }, { "Guardar", "Save" },

            // ---------------- categorías y optimizaciones
            { "Energía y CPU", "Power & CPU" }, { "Gráficos y juego", "Graphics & gaming" }, { "Red", "Network" }, { "Periféricos", "Peripherals" }, { "Sistema", "System" },
            { "Plan de energía Máximo Rendimiento", "Ultimate Performance power plan" },
            { "Crea un plan Ultimate sin core parking, con la CPU siempre a máxima frecuencia y sin suspensión USB.", "Creates an Ultimate plan with no core parking, the CPU always at full clock and no USB suspend." },
            { "Desactivar Power Throttling", "Disable Power Throttling" },
            { "Evita que Windows reduzca la CPU a procesos en segundo plano (Discord, OBS, overlays).", "Stops Windows from throttling the CPU for background processes (Discord, OBS, overlays)." },
            { "Prioridad de CPU para la ventana activa", "CPU priority for the active window" },
            { "Da quantums más cortos y prioridad al programa en primer plano (Win32PrioritySeparation = 0x26).", "Gives shorter quantums and priority to the foreground program (Win32PrioritySeparation = 0x26)." },
            { "Resolución de timer global", "Global timer resolution" },
            { "Permite que la resolución de 0.5 ms del Modo Juego afecte a todo el sistema en Windows 11. Mejora la estabilidad del frametime.", "Lets Game Mode's 0.5 ms resolution apply system-wide on Windows 11. Improves frametime stability." },
            { "Modo Juego de Windows", "Windows Game Mode" },
            { "Prioriza el juego y evita que Windows Update instale drivers o muestre notificaciones mientras juegas.", "Prioritizes the game and stops Windows Update from installing drivers or showing notifications while you play." },
            { "Desactivar grabación en segundo plano", "Disable background recording" },
            { "Apaga Xbox Game DVR, que graba continuamente y puede costar entre un 3 y un 10% de FPS.", "Turns off Xbox Game DVR, which records constantly and can cost 3-10% FPS." },
            { "Programación de GPU acelerada por hardware", "Hardware-accelerated GPU scheduling" },
            { "La GPU gestiona su propia memoria de vídeo: menor latencia y es necesaria para DLSS Frame Generation.", "The GPU manages its own video memory: lower latency, and required for DLSS Frame Generation." },
            { "Optimizaciones para juegos en ventana", "Optimizations for windowed games" },
            { "Los juegos en ventana sin bordes usan el modelo flip: latencia equivalente a pantalla completa y soporte de VRR.", "Borderless windowed games use the flip model: fullscreen-equivalent latency and VRR support." },
            { "Prioridad de juegos en el programador multimedia", "Game priority in the multimedia scheduler" },
            { "Reserva menos CPU para tareas de fondo (SystemResponsiveness 10) y sube la prioridad de los juegos en MMCSS.", "Reserves less CPU for background tasks (SystemResponsiveness 10) and raises game priority in MMCSS." },
            { "Desactivar optimizaciones de pantalla completa", "Disable fullscreen optimizations" },
            { "Fuerza pantalla completa exclusiva real. Ayuda en algunos juegos antiguos; en los modernos suele ser mejor dejarlo.", "Forces true exclusive fullscreen. Helps some older games; modern games are usually better without it." },
            { "Desactivar transparencias", "Disable transparency effects" },
            { "Quita el efecto de vidrio de Windows. Libera algo de GPU en equipos modestos o con gráficos integrados.", "Removes Windows' glass effect. Frees some GPU on modest PCs or integrated graphics." },
            { "Desactivar algoritmo de Nagle", "Disable Nagle's algorithm" },
            { "Envía los paquetes TCP al instante en lugar de agruparlos. Reduce la latencia en juegos que usan TCP.", "Sends TCP packets instantly instead of batching them. Lowers latency in games that use TCP." },
            { "Quitar limitación de red multimedia", "Remove multimedia network throttling" },
            { "Desactiva el límite de paquetes de red que Windows aplica cuando se reproduce audio o vídeo.", "Disables the network packet limit Windows applies while audio or video is playing." },
            { "DNS rápido (Cloudflare 1.1.1.1)", "Fast DNS (Cloudflare 1.1.1.1)" },
            { "Cambia el DNS de tus adaptadores a 1.1.1.1 / 1.0.0.1. Acelera la conexión a servidores y matchmaking; no cambia el ping en partida.", "Sets your adapters' DNS to 1.1.1.1 / 1.0.0.1. Speeds up connecting to servers and matchmaking; doesn't change in-game ping." },
            { "Desactivar aceleración del mouse", "Disable mouse acceleration" },
            { "Movimiento 1:1: la mira se mueve igual sin importar la velocidad de tu mano. Imprescindible para apuntar.", "1:1 movement: your crosshair moves the same regardless of hand speed. Essential for aiming." },
            { "Respuesta de teclado más rápida", "Faster keyboard response" }, { "Reduce el retardo de repetición de teclas al mínimo.", "Reduces key repeat delay to the minimum." },
            { "Desactivar atajo de Teclas especiales", "Disable the Sticky Keys shortcut" },
            { "Evita la ventana que aparece al pulsar Shift 5 veces en plena partida. Se aplica al reiniciar sesión.", "Prevents the popup when you press Shift 5 times mid-game. Applies after signing in again." },
            { "Desactivar apps en segundo plano", "Disable background apps" },
            { "Impide que las apps de la Tienda se ejecuten en segundo plano consumiendo CPU, RAM y red.", "Stops Store apps from running in the background and using CPU, RAM and network." },
            { "Desactivar telemetría (DiagTrack)", "Disable telemetry (DiagTrack)" },
            { "Detiene el servicio que envía datos de uso a Microsoft en segundo plano.", "Stops the service that sends usage data to Microsoft in the background." },
            { "Desactivar sugerencias y consejos de Windows", "Disable Windows suggestions and tips" },
            { "Quita notificaciones de 'sugerencias', anuncios y consejos que pueden aparecer en medio de una partida.", "Removes 'suggestion' notifications, ads and tips that can pop up mid-game." },
            { "Impacto alto", "High impact" }, { "Impacto medio", "Medium impact" }, { "Impacto bajo", "Low impact" }, { "Recomendado", "Recommended" }, { "Requiere reinicio", "Requires restart" },

            // ---------------- limpieza
            { "Archivos temporales del usuario", "User temporary files" }, { "Restos de instaladores, actualizaciones y programas (%TEMP%).", "Leftovers from installers, updates and programs (%TEMP%)." },
            { "Archivos temporales de Windows", "Windows temporary files" },
            { "Caché de arranque de programas. Windows la regenera; las apps tardan un poco más en abrir la primera vez.", "Program launch cache. Windows rebuilds it; apps take slightly longer to open the first time." },
            { "Volcados de memoria y reportes de errores", "Memory dumps and error reports" }, { "CrashDumps, Minidump y reportes de errores de Windows (WER).", "CrashDumps, Minidump and Windows Error Reporting (WER)." },
            { "Papelera de reciclaje", "Recycle Bin" }, { "Vacía la papelera de todas las unidades.", "Empties the Recycle Bin on all drives." },
            { "Caché DNS", "DNS cache" }, { "Resuelve problemas de conexión a servidores y fuerza direcciones actualizadas.", "Fixes server connection issues and forces up-to-date addresses." },
            { "Caché de Optimización de distribución", "Delivery Optimization cache" }, { "Actualizaciones de Windows ya instaladas que se comparten con otros PCs.", "Already-installed Windows updates shared with other PCs." },
            { "Descargas de Windows Update", "Windows Update downloads" }, { "Paquetes de actualización ya instalados. Seguro de borrar.", "Update packages that are already installed. Safe to delete." },
            { "Caché de navegadores", "Browser cache" }, { "Chrome, Edge, Brave, Opera GX y Firefox. Cierra el navegador para borrarla toda.", "Chrome, Edge, Brave, Opera GX and Firefox. Close the browser to clear all of it." },
            { "Caché de Discord y launchers", "Discord and launcher cache" }, { "Discord, Steam (web) y Epic Games Launcher (web).", "Discord, Steam (web) and Epic Games Launcher (web)." },
            { "Caché de shaders (DirectX / NVIDIA / AMD)", "Shader cache (DirectX / NVIDIA / AMD)" },
            { "Solo si tienes errores gráficos o tras cambiar drivers: la primera partida tendrá tirones mientras se regenera.", "Only if you have graphics glitches or after changing drivers: the first match will stutter while it rebuilds." },
            { "Última limpieza", "Last cleanup" }, { "{0} liberados", "{0} freed" }, { "pueden liberarse con la selección actual", "can be freed with the current selection" },
            { "Selecciona al menos una categoría", "Select at least one category" }, { "Limpiando {0} categorías...", "Cleaning {0} categories..." },
            { "Limpieza completa · {0} liberados", "Cleanup complete · {0} freed" },

            // ---------------- instalador / servicios
            { "Cerrando GameBoost...", "Closing GameBoost..." }, { "Copiando archivos...", "Copying files..." }, { "Creando accesos directos...", "Creating shortcuts..." },
            { "Registrando en Windows...", "Registering with Windows..." }, { "Instalación completada", "Installation complete" },
            { "Midiendo carga en segundo plano...", "Measuring background load..." }, { "Midiendo precisión del temporizador...", "Measuring timer precision..." },
            { "CPU: un núcleo...", "CPU: single core..." }, { "CPU: todos los núcleos...", "CPU: all cores..." }, { "Midiendo red...", "Measuring network..." },
            { "¿Quieres desinstalar GameBoost?", "Do you want to uninstall GameBoost?" },
            { "Sí: desinstalar y restaurar tu configuración original de Windows.", "Yes: uninstall and restore your original Windows settings." },
            { "No: desinstalar y mantener la configuración actual.", "No: uninstall and keep the current settings." },
            { "GameBoost se desinstaló correctamente.", "GameBoost was uninstalled successfully." },

            // ---------------- general
            { "Versión instalada", "Installed version" }, { "Versión portátil", "Portable version" }, { "iniciado", "started" },
            { "Abrir GameBoost", "Open GameBoost" }, { "Salir", "Exit" }, { "GameBoost sigue activo", "GameBoost is still running" },
            { "Está en la bandeja del sistema y activará el Modo Juego cuando abras un juego.", "It's in the system tray and will enable Game Mode when you open a game." },
            { "Perfil detectado", "Detected profile" }, { "Portátil", "Laptop" }, { "Sobremesa", "Desktop" }, { "Intel híbrido (P+E)", "Intel hybrid (P+E)" },
            { "Gráficos integrados", "Integrated graphics" }, { "GPU dedicada", "Dedicated GPU" }, { "Gráficos híbridos", "Hybrid graphics" },
            { "No recomendado en tu {0}: los X3D de doble CCD necesitan core parking y el plan Equilibrado para llevar los juegos a los núcleos con 3D V-Cache.", "Not recommended on your {0}: dual-CCD X3D chips need core parking and the Balanced plan to keep games on the 3D V-Cache cores." },
            { "CPU X3D de doble CCD: se mantiene el plan Equilibrado y no se cierra la Xbox Game Bar (el driver de V-Cache la necesita).", "Dual-CCD X3D CPU: the Balanced plan is kept and Xbox Game Bar is not closed (the V-Cache driver needs it)." },
            { "Intel con núcleos P y E: desactivar Power Throttling pasa a impacto alto, así el juego no termina en los E-cores.", "Intel with P and E cores: disabling Power Throttling becomes high impact so the game doesn't end up on E-cores." },
            { "No disponible en tu gráfica.", "Not available on your GPU." },
            { "Programación de GPU por hardware fuera de las recomendaciones: tu gráfica no la soporta.", "Hardware GPU scheduling removed from recommendations: your GPU doesn't support it." },
            { "Gráficos integrados: se recomienda quitar transparencias para liberar GPU y memoria compartida.", "Integrated graphics: disabling transparency is recommended to free GPU and shared memory." },
            { "Gráficos híbridos: cada juego que abras con GameBoost se asigna a la {0}.", "Hybrid graphics: every game you open with GameBoost is assigned to the {0}." },
            { "Portátil: aviso automático si juegas con batería y consejos de energía específicos.", "Laptop: automatic warning if you play on battery, plus laptop-specific power tips." },
            { "Windows en disco duro (HDD): no se pausa SysMain ni se borra Prefetch, porque aceleran la carga en discos mecánicos.", "Windows on a hard drive (HDD): SysMain isn't paused and Prefetch isn't cleared, because they speed up loading on mechanical drives." },
            { "{0} GB de RAM: liberación de RAM y cierre de apps activados en cada partida.", "{0} GB of RAM: RAM release and app closing enabled for every match." },
            { "NVIDIA: consejos de Reflex y del Panel de control, revisión del driver y NVIDIA App en Apps recomendadas.", "NVIDIA: Reflex and Control Panel tips, driver check and NVIDIA App in Recommended apps." },
            { "AMD Radeon: consejos de Anti-Lag 2 y Adrenalin, revisión del driver y AMD Software en Apps recomendadas.", "AMD Radeon: Anti-Lag 2 and Adrenalin tips, driver check and AMD Software in Recommended apps." },
            { "AMD Ryzen: se comprueban los drivers de chipset de AMD (mejoran el planificador y la gestión de energía).", "AMD Ryzen: AMD chipset drivers are checked (they improve the scheduler and power management)." },
            { "Buenos días", "Good morning" }, { "Buenas tardes", "Good afternoon" }, { "Buenas noches", "Good evening" },
            { "Liberar", "Free up" }, { "Memoria en espera (standby) liberada", "Standby memory released" }, { "Procesador", "Processor" }, { "Memoria RAM", "Memory" },
            { "Latencia de red", "Network latency" }, { "Disco del sistema", "System drive" }, { "{0} hilos", "{0} threads" }, { "Midiendo...", "Measuring..." },
            { "{0} de {1} GB en uso", "{0} of {1} GB in use" }, { "libres de {0} en {1}", "free of {0} on {1}" }, { "recién activado", "just enabled" },
            { "Sin red", "Offline" }, { "No hay respuesta de 1.1.1.1", "No response from 1.1.1.1" }, { "Excelente", "Excellent" }, { "Buena", "Good" },
            { "Regular", "Fair" }, { "Alta", "High" },
            { "{0} de {1} optimizaciones recomendadas activas", "{0} of {1} recommended optimizations active" }, { "Tu PC está lista para jugar.", "Your PC is ready to play." },
            { "Tu PC está optimizada al {0}%. Activa lo que falta con un clic.", "Your PC is {0}% optimized. Enable the rest with one click." },
            { "{0} de {1} ajustes activos · Todo es reversible con un clic.", "{0} of {1} tweaks active · Everything is reversible with one click." },
            { "{0}/{1} activos", "{0}/{1} active" }, { "Gráfica", "Graphics" }, { "Driver gráfico", "Graphics driver" }, { "Memoria", "Memory" },
            { "Pantalla", "Display" }, { "máx. {0} Hz", "max. {0} Hz" }, { "Encendido", "Uptime" },

            // ---------------- recomendaciones
            { "1 optimización recomendada sin aplicar", "1 recommended optimization not applied" }, { "{0} optimizaciones recomendadas sin aplicar", "{0} recommended optimizations not applied" },
            { "y {0} más.", "and {0} more." }, { "Aplicar", "Apply" }, { "Reinicia para completar los cambios", "Restart to complete the changes" },
            { "Algunos ajustes (programación de GPU, timer, teclas especiales) se activan tras reiniciar.", "Some tweaks (GPU scheduling, timer, Sticky Keys) take effect after a restart." },
            { "Tu pantalla está a {0} Hz pero soporta {1} Hz", "Your display is at {0} Hz but supports {1} Hz" },
            { "Estás perdiendo fluidez. Cámbialo en Configuración > Pantalla > Pantalla avanzada > Frecuencia de actualización.", "You're losing smoothness. Change it in Settings > Display > Advanced display > Refresh rate." },
            { "Abrir", "Open" }, { "Estás usando la batería", "You're on battery power" },
            { "En batería la CPU y la GPU bajan su rendimiento. Conecta el cargador para jugar.", "On battery, the CPU and GPU run slower. Plug in the charger to play." },
            { "Driver de gráficos de hace {0} meses", "Graphics driver is {0} months old" },
            { "Tienes el driver {0}. Los drivers nuevos traen más FPS y correcciones para juegos recientes.", "You have driver {0}. Newer drivers bring more FPS and fixes for recent games." },
            { "Actualizar", "Update" }, { "Instala los drivers de chipset de AMD", "Install the AMD chipset drivers" },
            { "Imprescindibles en tu {0}: incluyen el driver que envía los juegos a los núcleos con 3D V-Cache.", "Essential on your {0}: they include the driver that sends games to the 3D V-Cache cores." },
            { "Mejoran el planificador de núcleos y la gestión de energía de tu Ryzen.", "They improve your Ryzen's core scheduler and power management." },
            { "Descargar", "Download" }, { "{0} programas se inician con Windows", "{0} programs start with Windows" },
            { "Desactiva los que no uses (launchers, actualizadores, Spotify...) para tener más RAM y CPU libres.", "Disable the ones you don't use (launchers, updaters, Spotify...) to free up RAM and CPU." },
            { "Gestionar", "Manage" }, { "Tu RAM funciona a {0} MT/s", "Your RAM runs at {0} MT/s" },
            { "Si tu memoria es de mayor velocidad, activa el perfil XMP/EXPO en la BIOS: puede dar un 5-15% más de FPS en juegos que dependen de la CPU.", "If your memory is rated faster, enable the XMP/EXPO profile in the BIOS: it can give 5-15% more FPS in CPU-bound games." },
            { "Tienes {0} GB de RAM", "You have {0} GB of RAM" },
            { "Los juegos actuales piden 16 GB. Cierra el navegador antes de jugar; el Modo Juego ya cierra las apps en segundo plano.", "Current games ask for 16 GB. Close your browser before playing; Game Mode already closes background apps." },
            { "Windows está en un disco duro (HDD)", "Windows is on a hard drive (HDD)" },
            { "Un SSD es la mejora más notable que puedes hacer: cargas 5-10 veces más rápidas y menos tirones.", "An SSD is the most noticeable upgrade you can make: 5-10x faster loading and less stutter." },
            { "{0} en archivos basura", "{0} of junk files" }, { "Temporales, prefetch, volcados de errores, papelera y caché de actualizaciones.", "Temp files, prefetch, error dumps, Recycle Bin and update cache." },
            { "Limpiar", "Clean" }, { "Tu PC lleva {0} días encendida", "Your PC has been on for {0} days" },
            { "Reiniciar libera memoria, cierra procesos colgados y aplica actualizaciones pendientes. El 'Inicio rápido' no cuenta como reinicio.", "Restarting frees memory, closes hung processes and applies pending updates. 'Fast startup' shutdown doesn't count as a restart." },
            { "Poco espacio en {0} ({1} libres)", "Low space on {0} ({1} free)" },
            { "Con menos del 10-15% libre el SSD se vuelve más lento y Windows no puede actualizar bien.", "With less than 10-15% free, the SSD slows down and Windows can't update properly." },
            { "Mide el rendimiento de tu PC", "Measure your PC's performance" },
            { "Ejecuta un benchmark para tener una referencia y comparar después de optimizar.", "Run a benchmark to get a baseline and compare after optimizing." },
            { "Todo en orden", "All good" }, { "Tu PC está optimizada. Pulsa Jugar en cualquier juego y GameBoost hará el resto.", "Your PC is optimized. Press Play on any game and GameBoost does the rest." },
            { "{0} pendientes", "{0} pending" }, { "No se pudo abrir", "Couldn't open" },

            // ---------------- actualizaciones
            { "Buscando actualizaciones...", "Checking for updates..." }, { "GameBoost {0} disponible", "GameBoost {0} available" },
            { "Incluye mejoras y correcciones.", "Includes improvements and fixes." }, { "Abre GameBoost para actualizar.", "Open GameBoost to update." },
            { "No se pudo conectar con el servidor de actualizaciones", "Couldn't reach the update server" }, { "Ya tienes la última versión ({0})", "You already have the latest version ({0})" },
            { "Descargando... {0}%", "Downloading... {0}%" }, { "Instalando GameBoost {0}", "Installing GameBoost {0}" }, { "No se pudo descargar la actualización", "Couldn't download the update" },

            // ---------------- juegos
            { "Aún no hay juegos. Ve a Juegos y pulsa 'Buscar juegos' o 'Agregar juego'.", "No games yet. Go to Games and press 'Scan for games' or 'Add game'." },
            { "Programas", "Programs" }, { "Todos los archivos", "All files" }, { "Selecciona el ejecutable del juego", "Select the game executable" },
            { "¿Seguro? Pulsa otra vez", "Sure? Press again" }, { "Juego eliminado", "Game deleted" }, { "Buscando juegos en Steam, Epic y Riot...", "Scanning Steam, Epic and Riot for games..." },
            { "{0} juegos detectados", "{0} games found" }, { "{0} nuevos", "{0} new" }, { "{0} juegos nuevos detectados", "{0} new games found" },
            { "No hay juegos todavía. Búscalos automáticamente o agrégalos a mano.", "No games yet. Scan for them automatically or add them manually." },
            { "1 juego", "1 game" }, { "{0} juegos", "{0} games" },
            { "Pulsa ▶ y GameBoost optimiza la PC, cierra lo que sobra y abre el juego.", "Press ▶ and GameBoost optimizes your PC, closes what's not needed and launches the game." },
            { "Manual", "Manual" }, { "Jugado", "Played" }, { "Sin proceso configurado", "No process set" }, { "Editar", "Edit" }, { "Optimizar y jugar", "Optimize and play" },
            { "hace {0} min", "{0} min ago" }, { "hace {0} h", "{0} h ago" }, { "ayer", "yesterday" }, { "hace {0} días", "{0} days ago" },
            { "Pon al menos el nombre y el ejecutable o URL", "Enter at least the name and the executable or URL" }, { "Guardado", "Saved" },

            // ---------------- optimizaciones (acciones)
            { "No se pudo cambiar '{0}'", "Couldn't change '{0}'" }, { "Activado", "Enabled" }, { "Desactivado", "Disabled" }, { "requiere reinicio", "requires restart" },
            { "Ya tienes todas las optimizaciones recomendadas", "You already have all recommended optimizations" }, { "Aplicando {0} optimizaciones...", "Applying {0} optimizations..." },
            { "{0} optimizaciones aplicadas", "{0} optimizations applied" }, { "reinicia para completar", "restart to complete" }, { "No hay ajustes que restaurar", "Nothing to restore" },
            { "Restaurando valores de Windows...", "Restoring Windows defaults..." }, { "{0} ajustes restaurados a los valores de Windows", "{0} tweaks reset to Windows defaults" },
            { "Windows restaurado a sus valores por defecto", "Windows restored to its default values" },

            // ---------------- benchmark
            { "CPU · un núcleo", "CPU · single core" }, { "CPU · multinúcleo", "CPU · multi-core" }, { "Temporizador", "Timer" }, { "CPU en segundo plano", "Background CPU" },
            { "Procesos activos", "Running processes" }, { "RAM libre", "Free RAM" }, { "Referencia actualizada", "Baseline updated" },
            { "El historial aparecerá aquí.", "Your history will appear here." }, { "Comparado con tu referencia del {0}", "Compared with your baseline from {0}" },
            { "medido con Modo Juego activo", "measured with Game Mode on" },
            { "Este resultado es tu referencia. Optimiza tu PC o activa el Modo Juego y vuelve a medir para ver la mejora.", "This result is your baseline. Optimize your PC or enable Game Mode and run it again to see the improvement." },
            { "Referencia", "Baseline" }, { "Modo Juego", "Game Mode" }, { "CPU {0} / {1} · Temporizador {2} ms · Ping {3} ms", "CPU {0} / {1} · Timer {2} ms · Ping {3} ms" },
            { "Benchmark completado · {0} puntos", "Benchmark complete · {0} points" },

            // ---------------- ajustes generales
            { "Reiniciar ahora", "Restart now" }, { "Automático", "Automatic" }, { "Idioma", "Language" }, { "Se aplica al reiniciar GameBoost.", "Applies after restarting GameBoost." },
            { "Detectar juegos automáticamente", "Detect games automatically" },
            { "Activa el Modo Juego solo cuando abres un juego, aunque lo inicies desde Steam, Epic o un acceso directo.", "Turns on Game Mode by itself when you open a game, even if you launch it from Steam, Epic or a shortcut." },
            { "Seguir en la bandeja al cerrar", "Keep running in the tray when closed" },
            { "Al cerrar la ventana, GameBoost sigue en la bandeja del sistema para detectar tus juegos.", "When you close the window, GameBoost stays in the system tray to detect your games." },
            { "GameBoost se iniciará con Windows (en la bandeja)", "GameBoost will start with Windows (in the tray)" }, { "GameBoost ya no se iniciará con Windows", "GameBoost will no longer start with Windows" },
            { "Iniciar con Windows", "Start with Windows" }, { "Arranca minimizado en la bandeja al iniciar sesión, listo para detectar tus juegos.", "Starts minimized in the tray when you sign in, ready to detect your games." },
            { "Buscar actualizaciones automáticamente", "Check for updates automatically" }, { "Comprueba al iniciar si hay una versión nueva de GameBoost.", "Checks for a new GameBoost version at startup." },
            { "Desinstalar", "Uninstall" }, { "Actualizar instalación", "Update installation" }, { "Instalar en este PC", "Install on this PC" }, { "Instalado en este PC", "Installed on this PC" },
            { "Instálalo para tener acceso en el menú Inicio, inicio con Windows y desinstalador en Configuración > Aplicaciones.", "Install it to get a Start menu entry, start with Windows and an uninstaller in Settings > Apps." },
            { "Instalando GameBoost...", "Installing GameBoost..." }, { "Instalado. Abriendo la versión instalada...", "Installed. Opening the installed version..." },
            { "GameBoost instalado en", "GameBoost installed in" },
            { "Todavía no hay copia: se crea automáticamente la primera vez que GameBoost cambia un ajuste.", "No backup yet: it's created automatically the first time GameBoost changes a setting." },
            { "Copia creada el {0} · {1} valores originales guardados. Devuelve Windows exactamente a como estaba antes de GameBoost.", "Backup created on {0} · {1} original values saved. Returns Windows exactly to how it was before GameBoost." },
            { "Restaurando tu configuración original...", "Restoring your original settings..." }, { "Configuración original restaurada · {0} valores", "Original settings restored · {0} values" },
            { "Creando punto de restauración (puede tardar un minuto)...", "Creating restore point (this may take a minute)..." }, { "Punto de restauración creado", "Restore point created" },
            { "Punto de restauración", "Restore point" }, { "Windows solo permite 1 punto cada 24 h o la protección está desactivada", "Windows only allows 1 restore point every 24 h, or System Protection is off" },

            // ---------------- ajustes del Modo Juego
            { "Aplicar optimizaciones recomendadas", "Apply recommended optimizations" }, { "Activa automáticamente los ajustes recomendados para tu PC que falten.", "Automatically enables any missing tweaks recommended for your PC." },
            { "Limpiar temporales", "Clean temp files" }, { "Borra temporales y volcados de errores antes de abrir el juego.", "Deletes temp files and error dumps before launching the game." },
            { "Cerrar apps en segundo plano", "Close background apps" }, { "Cierra las apps de la lista de abajo (OneDrive, Teams, Widgets...).", "Closes the apps in the list below (OneDrive, Teams, Widgets...)." },
            { "Reabrir apps al terminar", "Reopen apps afterwards" }, { "Vuelve a abrir las apps cerradas cuando sales del juego.", "Reopens the closed apps when you quit the game." },
            { "Pausar servicios de Windows", "Pause Windows services" }, { "Detiene indexación, telemetría y Windows Update durante la partida.", "Stops indexing, telemetry and Windows Update while you play." },
            { "Liberar memoria RAM en espera", "Release standby RAM" }, { "Vacía la lista standby para que el juego tenga más RAM libre al cargar.", "Clears the standby list so the game has more free RAM when loading." },
            { "Limpiar caché DNS", "Flush DNS cache" }, { "Evita fallos de conexión a servidores con direcciones antiguas.", "Avoids connection failures to servers with stale addresses." },
            { "Timer del sistema a 0.5 ms", "0.5 ms system timer" }, { "Mejora la precisión del frametime y del input mientras dure la sesión.", "Improves frametime and input precision for the whole session." },
            { "Restaurar todo al cerrar el juego", "Restore everything when the game closes" }, { "Reanuda servicios y apps automáticamente cuando el juego se cierra.", "Resumes services and apps automatically when the game closes." },
            { "Minimizar GameBoost al jugar", "Minimize GameBoost while playing" }, { "Se oculta en la barra de tareas al lanzar el juego.", "Hides in the taskbar when the game launches." },

            // ---------------- consejos
            { "NVIDIA Reflex: Activado + Boost", "NVIDIA Reflex: On + Boost" },
            { "En Fortnite, Valorant, CS2, Apex y COD. Es la mayor reducción de latencia que existe: hasta un 30-50% menos.", "In Fortnite, Valorant, CS2, Apex and COD. It's the biggest latency reduction available: up to 30-50% lower." },
            { "Panel de control NVIDIA", "NVIDIA Control Panel" },
            { "Administración de energía: 'Preferir rendimiento máximo'. Modo de baja latencia: 'Activado' en juegos sin Reflex. Tamaño de caché de shaders: 10 GB.", "Power management: 'Prefer maximum performance'. Low Latency Mode: 'On' for games without Reflex. Shader cache size: 10 GB." },
            { "DLSS en tu {0}", "DLSS on your {0}" }, { "Activa DLSS en modo Calidad o Equilibrado: más FPS con una imagen casi idéntica a la nativa.", "Enable DLSS in Quality or Balanced mode: more FPS with an image almost identical to native." },
            { "Tu GPU también soporta Frame Generation.", "Your GPU also supports Frame Generation." },
            { "Actívalo en Adrenalin (y en el juego si lo soporta). Desactiva Radeon Chill y Enhanced Sync para mínima latencia.", "Enable it in Adrenalin (and in-game if supported). Turn off Radeon Chill and Enhanced Sync for minimum latency." },
            { "Adrenalin: perfil eSports", "Adrenalin: eSports profile" }, { "Usa el perfil gráfico 'eSports' y FSR en los juegos que lo incluyan para ganar FPS.", "Use the 'eSports' graphics profile and FSR in games that include it for extra FPS." },
            { "Usa FSR/XeSS en modo Rendimiento, resolución 1080p o menor y, si tu BIOS lo permite, asigna más memoria de vídeo (UMA Frame Buffer).", "Use FSR/XeSS in Performance mode, 1080p or lower and, if your BIOS allows it, assign more video memory (UMA Frame Buffer)." },
            { "Tu {0}", "Your {0}" }, { "Mantén el plan Equilibrado, la Game Bar y los drivers de chipset de AMD al día: así los juegos usan el CCD con 3D V-Cache.", "Keep the Balanced plan, Game Bar and up-to-date AMD chipset drivers: that way games use the CCD with 3D V-Cache." },
            { "Pantalla de {0} Hz", "{0} Hz display" }, { "Ahora está a {0} Hz: súbela a {1} Hz en Configuración > Pantalla > Pantalla avanzada.", "It's currently at {0} Hz: raise it to {1} Hz in Settings > Display > Advanced display." },
            { "Con G-Sync/FreeSync limita los FPS a {0}; sin sincronización, déjalos sin límite.", "With G-Sync/FreeSync cap FPS at {0}; without sync, leave them uncapped." },
            { "Usa cable DisplayPort o HDMI 2.0+.", "Use a DisplayPort or HDMI 2.0+ cable." }, { "{0} GB de RAM", "{0} GB of RAM" },
            { "Cierra el navegador antes de jugar: Chrome puede ocupar 1-3 GB. El Modo Juego ya cierra las apps en segundo plano.", "Close your browser before playing: Chrome can use 1-3 GB. Game Mode already closes background apps." },
            { "Juega enchufado y sobre una superficie dura o una base con ventilación: el calor es lo que más rendimiento le quita a un portátil.", "Play plugged in and on a hard surface or cooling pad: heat is what costs a laptop the most performance." },
            { "Si tu marca tiene modo 'GPU dedicada / MUX', actívalo para unos FPS extra.", "If your brand has a 'dedicated GPU / MUX' mode, enable it for extra FPS." },
            { "PARA TU EQUIPO", "FOR YOUR PC" }, { "FORTNITE Y SHOOTERS COMPETITIVOS", "FORTNITE AND COMPETITIVE SHOOTERS" },
            { "Modo de renderizado", "Rendering mode" },
            { "Fortnite: 'Rendimiento' da el máximo de FPS en PCs medias. DirectX 12 es mejor con GPU moderna, pero la primera partida compila shaders.", "Fortnite: 'Performance' gives maximum FPS on mid-range PCs. DirectX 12 is better with a modern GPU, but the first match compiles shaders." },
            { "Limita los FPS", "Cap your FPS" }, { "Un tope estable (Hz del monitor o un poco más) da un frametime más parejo que FPS altos que suben y bajan.", "A stable cap (your monitor's Hz or a bit more) gives smoother frametimes than high FPS that swing up and down." },
            { "Pantalla completa", "Fullscreen" }, { "Usa pantalla completa o ventana sin bordes con 'Optimizaciones para juegos en ventana' activado (pestaña Optimizar).", "Use fullscreen, or borderless windowed with 'Optimizations for windowed games' on (Optimize tab)." },
            { "Calidad baja donde no se nota", "Low settings where it doesn't show" }, { "Sombras, efectos, post-procesado y hierba en bajo: más FPS y mejor visibilidad. Texturas según la VRAM de tu GPU.", "Shadows, effects, post-processing and grass on low: more FPS and better visibility. Textures according to your GPU's VRAM." },
            { "Mouse a 1000 Hz o más", "Mouse at 1000 Hz or more" }, { "Configura el polling rate en el software de tu mouse y usa la sensibilidad de Windows en 6/11 (por defecto).", "Set the polling rate in your mouse software and keep Windows sensitivity at 6/11 (default)." },
            { "Verifica los archivos del juego", "Verify game files" }, { "Si tienes tirones o crasheos: Epic > Biblioteca > ... > Administrar > Verificar. En Steam: Propiedades > Archivos instalados.", "If you get stutters or crashes: Epic > Library > ... > Manage > Verify. On Steam: Properties > Installed Files." },
            { "WINDOWS Y HARDWARE", "WINDOWS AND HARDWARE" }, { "Activa XMP / EXPO en la BIOS", "Enable XMP / EXPO in the BIOS" },
            { "La RAM viene por defecto a velocidad básica. Activar su perfil puede dar un 5-15% más de FPS en juegos que dependen de la CPU.", "RAM runs at a basic speed by default. Enabling its profile can give 5-15% more FPS in CPU-bound games." },
            { "Drivers de GPU al día", "Keep GPU drivers up to date" }, { "Actualiza desde NVIDIA App / AMD Adrenalin. Si hay problemas, haz una instalación limpia con DDU en modo seguro.", "Update from NVIDIA App / AMD Adrenalin. If you have problems, do a clean install with DDU in safe mode." },
            { "Vigila las temperaturas", "Watch your temperatures" }, { "CPU por encima de 90 °C o GPU por encima de 85 °C provocan throttling. Limpia el polvo y revisa la pasta térmica cada 2-3 años.", "A CPU above 90 °C or GPU above 85 °C will throttle. Clean out dust and check the thermal paste every 2-3 years." },
            { "Juegos en SSD / NVMe", "Games on SSD / NVMe" }, { "Cargas mucho más rápidas y menos tirones al cargar zonas. Deja al menos un 15% del disco libre.", "Much faster loading and less stutter when loading areas. Keep at least 15% of the drive free." },
            { "Reinicia de verdad", "Do a real restart" }, { "Apagar con 'Inicio rápido' no limpia la memoria del kernel. Reinicia la PC cada pocos días o antes de una sesión larga.", "Shutting down with 'Fast startup' doesn't clear kernel memory. Restart your PC every few days or before a long session." },
            { "Evita los 'limpiadores de registro'", "Avoid 'registry cleaners'" }, { "No mejoran el rendimiento y pueden romper Windows. Un solo antivirus (Defender es suficiente).", "They don't improve performance and can break Windows. Use a single antivirus (Defender is enough)." },
            { "RED", "NETWORK" }, { "Usa cable Ethernet", "Use an Ethernet cable" }, { "El Wi-Fi añade jitter y pérdida de paquetes. Si no puedes, usa 5 GHz y ponte cerca del router.", "Wi-Fi adds jitter and packet loss. If you can't, use 5 GHz and stay close to the router." },
            { "Elige bien la región", "Pick the right region" }, { "Juega en el servidor con menor ping (en Fortnite: Configuración > Juego > Región de matchmaking).", "Play on the server with the lowest ping (in Fortnite: Settings > Game > Matchmaking Region)." },
            { "Cuidado con las descargas", "Watch out for downloads" }, { "Steam, Epic, OneDrive y Windows Update descargando en segundo plano disparan el ping. El Modo Juego pausa Windows Update.", "Steam, Epic, OneDrive and Windows Update downloading in the background spike your ping. Game Mode pauses Windows Update." },
            { "QoS en el router", "QoS on your router" }, { "Si compartes conexión, activa QoS / prioridad para gaming en el router para la IP de tu PC.", "If you share your connection, enable QoS / gaming priority on your router for your PC's IP." },

            // ---------------- apps recomendadas
            { "Gratis", "Free" }, { "Gratis / Pro", "Free / Pro" }, { "De pago", "Paid" }, { "Para tu PC", "For your PC" }, { "Instalado", "Installed" }, { "Sitio web", "Website" },
            { "DRIVERS PARA TU EQUIPO", "DRIVERS FOR YOUR PC" }, { "MONITOREO", "MONITORING" }, { "RENDIMIENTO Y MANTENIMIENTO", "PERFORMANCE AND MAINTENANCE" }, { "GRABAR Y TRANSMITIR", "RECORDING AND STREAMING" },
            { "Drivers Game Ready, ajustes óptimos por juego, filtros, ShadowPlay para grabar y overlay de rendimiento.", "Game Ready drivers, optimal per-game settings, filters, ShadowPlay recording and a performance overlay." },
            { "Drivers Radeon con Anti-Lag 2, HYPR-RX, Radeon Boost, FSR a nivel de driver y grabación ReLive.", "Radeon drivers with Anti-Lag 2, HYPR-RX, Radeon Boost, driver-level FSR and ReLive recording." },
            { "Drivers de chipset AMD", "AMD chipset drivers" }, { "Mejoran el planificador de núcleos y la energía de los Ryzen. Imprescindibles en los X3D.", "Improve Ryzen core scheduling and power management. Essential on X3D chips." },
            { "Detecta y actualiza drivers de chipset, gráficos, Wi-Fi y Bluetooth de Intel.", "Detects and updates Intel chipset, graphics, Wi-Fi and Bluetooth drivers." },
            { "Overlay con FPS, frametime, temperaturas y uso en el juego. RivaTuner es el limitador de FPS más estable que existe.", "In-game overlay with FPS, frametime, temperatures and usage. RivaTuner is the most stable FPS limiter there is." },
            { "Todos los sensores del PC: temperaturas, consumo y si la CPU o GPU están haciendo throttling.", "Every sensor in your PC: temperatures, power draw and whether the CPU or GPU is throttling." },
            { "Comprueba la velocidad real de tu RAM (si XMP/EXPO está activo) y los datos exactos de tu CPU.", "Checks your RAM's real speed (whether XMP/EXPO is on) and your CPU's exact details." },
            { "Salud y temperatura de tus SSD y discos. Te avisa antes de que un disco falle.", "Health and temperature of your SSDs and drives. Warns you before a drive fails." },
            { "Mantiene la prioridad y afinidad de cada juego de forma automática y evita que otros procesos le roben CPU.", "Keeps each game's priority and affinity automatically and stops other processes from stealing CPU." },
            { "Borra por completo el driver de la GPU antes de reinstalarlo. La solución a crasheos y tirones tras actualizar.", "Completely removes the GPU driver before reinstalling. The fix for crashes and stutters after updating." },
            { "Desinstala bloatware y programas en lote, y limpia los restos que dejan.", "Uninstalls bloatware and programs in bulk, and cleans up their leftovers." },
            { "Frame generation y escalado para cualquier juego. Muy útil en portátiles y GPUs modestas para duplicar FPS.", "Frame generation and upscaling for any game. Great on laptops and modest GPUs to double FPS." },
            { "Graba y transmite usando el encoder de tu GPU ({0}) casi sin perder FPS. Reemplaza al Game DVR que desactivamos.", "Record and stream using your GPU's encoder ({0}) with almost no FPS loss. Replaces the Game DVR we disabled." },

            // ---------------- sesión
            { "Este juego no tiene ejecutable o URL. Edítalo primero.", "This game has no executable or URL. Edit it first." }, { "Espera, estoy preparando la sesión...", "Hold on, preparing the session..." },
            { "Modo Juego activo", "Game Mode on" }, { "Desactivar Modo Juego", "Disable Game Mode" }, { "Preparando tu PC para {0}...", "Getting your PC ready for {0}..." },
            { "Activando Modo Juego...", "Enabling Game Mode..." }, { "{0} ajustes", "{0} tweaks" }, { "{0} limpiados", "{0} cleaned" }, { "{0} apps cerradas", "{0} apps closed" },
            { "{0} servicios pausados", "{0} services paused" }, { "RAM liberada", "RAM released" }, { "Modo Juego activado", "Game Mode enabled" },
            { "Modo Juego activado para {0}", "Game Mode enabled for {0}" }, { "GPU de alto rendimiento asignada a {0}", "High-performance GPU assigned to {0}" },
            { "Lanzando {0}", "Launching {0}" }, { "Abriendo {0}...", "Opening {0}..." }, { "No se pudo abrir {0}", "Couldn't open {0}" },
            { "No se detectó {0}.exe en 5 minutos", "{0}.exe wasn't detected within 5 minutes" },
            { "GPU dedicada asignada a {0} (se aplica desde la próxima vez que abras el juego)", "Dedicated GPU assigned to {0} (applies the next time you open the game)" },
            { "Prioridad {0} aplicada a {1}.exe", "{0} priority applied to {1}.exe" },
            { "Prioridad de {0} aplicada por registro (el anti-cheat bloquea el cambio en vivo)", "{0} priority applied via registry (the anti-cheat blocks live changes)" },
            { "Jugando {0}", "Playing {0}" }, { "{0} cerrado tras {1} min", "{0} closed after {1} min" },
            { "Modo Juego desactivado · sesión de {0} min", "Game Mode disabled · {0} min session" }, { "servicios reanudados", "services resumed" },
            { "Modo Juego desactivado", "Game Mode disabled" }, { "{0} detectado: activando Modo Juego", "{0} detected: enabling Game Mode" },

            // ---------------- instalador (Setup)
            { "Instalar GameBoost", "Install GameBoost" }, { "Optimizador de PC para juegos", "PC optimizer for gaming" },
            { "Crear acceso directo en el escritorio", "Create a desktop shortcut" }, { "Iniciar con Windows (en la bandeja)", "Start with Windows (in the tray)" },
            { "Se instalará en", "Will be installed in" }, { "Instalar", "Install" }, { "Reinstalar / actualizar", "Reinstall / update" },
            { "Abrir GameBoost ahora", "Open GameBoost now" }, { "Error durante la instalación", "Installation error" }, { "Ya está instalado. Puedes reinstalar o actualizar.", "Already installed. You can reinstall or update." },
            { "Al instalar aceptas que GameBoost modifique ajustes de Windows. Todos los cambios son reversibles.", "By installing you agree that GameBoost changes Windows settings. All changes are reversible." },
        };
    }
}
