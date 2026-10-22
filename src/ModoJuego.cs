using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace OptimizadorPC
{
    /// <summary>Un programa abierto que el modo juego puede cerrar.</summary>
    class ProgramaFondo
    {
        public string Proceso;      // nombre del proceso (sin .exe)
        public string Nombre;       // nombre que entiende cualquiera
        public string Motivo;       // por qué conviene cerrarlo
        public bool Recomendado;    // sale marcado de entrada
        public bool Reabrir;        // se vuelve a abrir al desactivar el modo juego
        public long Ram;
        public int Instancias;
        public string Ruta;         // .exe para volver a abrirlo
    }

    /// <summary>
    /// Modo juego: cierra programas de fondo, pone el plan de energía en alto rendimiento y activa el
    /// Modo de juego de Windows. Al desactivarlo deja todo como estaba. NUNCA cierra Discord, navegadores,
    /// Riot/Vanguard, Steam, juegos ni nada de Windows. Bloquea: llamar dentro de Task.Run.
    /// </summary>
    static class ModoJuego
    {
        private const string AltoRendimiento = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string Equilibrado = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string Economizador = "a1841308-3541-4fab-bc81-f71556f20b4a";
        private const string ClaveJuego = @"Software\Microsoft\GameBar";

        // Lo que nunca se cierra (se compara el nombre del proceso sin distinguir mayúsculas; "*" = empieza por)
        private static readonly string[] NuncaCerrar =
        {
            // Pedido expreso: Discord y navegadores
            "discord*", "chrome", "msedge", "msedgewebview2", "firefox", "brave", "opera*", "vivaldi", "iexplore", "arc", "zen",
            // Juegos y lanzadores de juegos en uso
            "riot*", "leagueclient*", "league of legends", "valorant*", "vgc", "vgtray", "steam*", "gameoverlayui", "javaw", "minecraft*",
            "robloxplayer*", "fortnite*", "easyanticheat*", "beservice", "battleye*",
            // Audio, grabación, drivers y periféricos
            "obs*", "nvidia*", "nvcontainer", "nvdisplay*", "amd*", "radeon*", "razer*", "rz*", "lghub*", "icue*", "steelseries*",
            "audiodg", "realtek*", "rtkaud*", "voicemeeter*", "mousex",
            // Esta app y la consola
            "optimizadorpc*", "pruebasoptimizador", "powershell", "pwsh", "cmd", "conhost", "windowsterminal", "openconsole", "claude"
        };

        // Programas de fondo conocidos: proceso, nombre, motivo, recomendado, reabrir al terminar
        private static readonly object[][] Conocidos =
        {
            new object[] { "OneDrive", "OneDrive", "Sincroniza archivos y usa disco e internet", true, true },
            new object[] { "GoogleDriveFS", "Google Drive", "Sincroniza archivos y usa disco e internet", true, true },
            new object[] { "Dropbox", "Dropbox", "Sincroniza archivos y usa disco e internet", true, true },
            new object[] { "Teams", "Microsoft Teams", "Usa mucha memoria aunque no lo uses", true, false },
            new object[] { "ms-teams", "Microsoft Teams", "Usa mucha memoria aunque no lo uses", true, false },
            new object[] { "Skype", "Skype", "Se queda abierto de fondo", true, false },
            new object[] { "Creative Cloud", "Adobe Creative Cloud", "Actualizador de Adobe de fondo", true, false },
            new object[] { "CCXProcess", "Adobe Creative Cloud", "Actualizador de Adobe de fondo", true, false },
            new object[] { "AdobeIPCBroker", "Adobe Creative Cloud", "Actualizador de Adobe de fondo", true, false },
            new object[] { "CoreSync", "Adobe Creative Cloud", "Actualizador de Adobe de fondo", true, false },
            new object[] { "EpicGamesLauncher", "Epic Games Launcher", "Lanzador de juegos (si no juegas a uno de Epic)", true, false },
            new object[] { "EADesktop", "EA app", "Lanzador de juegos (si no juegas a uno de EA)", true, false },
            new object[] { "upc", "Ubisoft Connect", "Lanzador de juegos (si no juegas a uno de Ubisoft)", true, false },
            new object[] { "GalaxyClient", "GOG Galaxy", "Lanzador de juegos (si no juegas a uno de GOG)", true, false },
            new object[] { "Battle.net", "Battle.net", "Lanzador de juegos (si no juegas a uno de Blizzard)", false, false },
            new object[] { "ollama", "Ollama", "IA local: puede ocupar varios GB de RAM", true, false },
            new object[] { "ollama app", "Ollama", "IA local: puede ocupar varios GB de RAM", true, false },
            new object[] { "PhoneExperienceHost", "Enlace Móvil", "Conexión con el móvil de fondo", true, false },
            new object[] { "Widgets", "Widgets de Windows", "Noticias y tiempo de fondo", true, false },
            new object[] { "WidgetService", "Widgets de Windows", "Noticias y tiempo de fondo", true, false },
            new object[] { "Spotify", "Spotify", "Solo si no escuchas música mientras juegas", false, false },
            new object[] { "WhatsApp", "WhatsApp", "Solo si no lo necesitas mientras juegas", false, false },
            new object[] { "WhatsApp.Root", "WhatsApp", "Solo si no lo necesitas mientras juegas", false, false },
            new object[] { "Telegram", "Telegram", "Solo si no lo necesitas mientras juegas", false, false },
            new object[] { "CurseForge", "CurseForge", "Solo si no lo usas con el juego", false, false },
            new object[] { "Overwolf", "Overwolf", "Solo si no lo usas con el juego", false, false },
            new object[] { "Docker Desktop", "Docker Desktop", "Usa mucha memoria", false, false }
        };

        private static string ArchivoEstado { get { return Path.Combine(Util.CarpetaDatos, "modojuego.txt"); } }

        public static bool Activo { get { return File.Exists(ArchivoEstado); } }

        /// <summary>Lo que se cerró y se volverá a abrir (si el modo juego está activo).</summary>
        public static List<string> CerradosAlActivar()
        {
            List<string> l = new List<string>();
            foreach (string linea in LeerEstado())
            {
                if (linea.StartsWith("cerrado\t")) l.Add(linea.Substring(8));
            }
            return l;
        }

        public static bool EsIntocable(string proceso)
        {
            string p = proceso.ToLowerInvariant();
            foreach (string n in NuncaCerrar)
            {
                if (n.EndsWith("*") ? p.StartsWith(n.TrimEnd('*')) : p == n) return true;
            }
            return false;
        }

        /// <summary>Programas abiertos que se pueden cerrar: los conocidos y los que tienen ventana y gastan memoria.</summary>
        public static List<ProgramaFondo> Candidatos()
        {
            Dictionary<string, ProgramaFondo> porNombre = new Dictionary<string, ProgramaFondo>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> protegidos = new HashSet<string>(PaginaProcesosProtegidos(), StringComparer.OrdinalIgnoreCase);
            int yo = Process.GetCurrentProcess().Id;
            int sesion = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == yo || p.SessionId != sesion) continue;
                    string nombre = p.ProcessName;
                    if (EsIntocable(nombre) || protegidos.Contains(nombre)) continue;
                    object[] conocido = Buscar(nombre);
                    bool ventana = p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle);
                    long ram = 0;
                    try { ram = p.WorkingSet64; } catch { }
                    string clave = conocido != null ? (string)conocido[1] : nombre;
                    ProgramaFondo f;
                    if (!porNombre.TryGetValue(clave, out f))
                    {
                        if (conocido == null && !(ventana && ram >= 150L * 1024 * 1024)) continue;
                        f = new ProgramaFondo();
                        f.Proceso = nombre;
                        f.Nombre = conocido != null ? (string)conocido[1] : NombreVisible(p);
                        f.Motivo = conocido != null ? (string)conocido[2] : "Programa abierto";
                        f.Recomendado = conocido != null && (bool)conocido[3];
                        f.Reabrir = conocido != null ? (bool)conocido[4] : ventana;
                        porNombre[clave] = f;
                    }
                    if (f.Proceso.IndexOf(nombre, StringComparison.OrdinalIgnoreCase) < 0) f.Proceso += "|" + nombre;
                    f.Ram += ram;
                    f.Instancias++;
                    if (f.Ruta == null && (ventana || conocido != null))
                    {
                        try { f.Ruta = p.MainModule.FileName; } catch { }
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            List<ProgramaFondo> l = new List<ProgramaFondo>(porNombre.Values);
            l.Sort(delegate(ProgramaFondo a, ProgramaFondo b)
            {
                if (a.Recomendado != b.Recomendado) return a.Recomendado ? -1 : 1;
                return b.Ram.CompareTo(a.Ram);
            });
            return l;
        }

        private static string[] PaginaProcesosProtegidos()
        {
            return new[]
            {
                "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost",
                "explorer", "sihost", "ctfmon", "MsMpEng", "SecurityHealthService", "SecurityHealthSystray", "spoolsv", "Memory Compression",
                "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "SearchIndexer", "TextInputHost",
                "WmiPrvSE", "NisSrv", "LsaIso", "dllhost", "taskhostw", "smartscreen", "ApplicationFrameHost", "SystemSettings",
                "TabTip", "LockApp", "GameBar", "GameBarFTServer", "XboxGameBarWidgets"
            };
        }

        private static object[] Buscar(string proceso)
        {
            foreach (object[] c in Conocidos)
            {
                if (string.Equals((string)c[0], proceso, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        private static string NombreVisible(Process p)
        {
            try
            {
                string d = p.MainModule.FileVersionInfo.FileDescription;
                if (!string.IsNullOrEmpty(d) && d.Trim().Length > 1) return d.Trim();
            }
            catch { }
            return p.ProcessName;
        }

        // ------------------------------------------------------------------ activar / desactivar

        /// <summary>Activa el modo juego. Devuelve un resumen de lo hecho para mostrarlo al usuario.</summary>
        public static List<string> Activar(List<ProgramaFondo> cerrar)
        {
            List<string> hecho = new List<string>();
            StringBuilder estado = new StringBuilder();

            // 1) Plan de energía: solo se sube si está en Equilibrado o Economizador (nunca se baja de
            //    "Máximo rendimiento" u otro plan del fabricante). Se guarda el actual para devolverlo después.
            string planAntes = PlanActual();
            bool planBajo = planAntes != null && (string.Equals(planAntes, Equilibrado, StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(planAntes, Economizador, StringComparison.OrdinalIgnoreCase));
            if (!planBajo)
            {
                hecho.Add("Plan de energía: ya estaba en " + NombrePlanActual() + " (no se toca)");
            }
            else
            {
                ResultadoProceso r = Util.EjecutarProceso(Util.RutaSistema("powercfg.exe"), "/setactive " + AltoRendimiento, 20000);
                if (r.Ok)
                {
                    estado.AppendLine("plan\t" + planAntes);
                    hecho.Add("Plan de energía: Alto rendimiento");
                }
                else
                {
                    hecho.Add("Este PC no tiene el plan Alto rendimiento: se deja el plan de energía como estaba");
                }
            }

            // 2) Modo de juego de Windows
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClaveJuego))
                {
                    object antes = k.GetValue("AutoGameModeEnabled");
                    estado.AppendLine("juego\t" + (antes == null ? "nada" : Convert.ToString(antes)));
                    k.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                }
                hecho.Add("Modo de juego de Windows activado");
            }
            catch { }

            // 3) Cerrar programas de fondo (primero con amabilidad y, si no responden, a la fuerza)
            foreach (ProgramaFondo f in cerrar)
            {
                int cerrados = 0;
                foreach (string nombre in f.Proceso.Split('|'))
                {
                    if (EsIntocable(nombre)) continue;   // red de seguridad
                    foreach (Process p in Process.GetProcessesByName(nombre))
                    {
                        try
                        {
                            if (p.Id == Process.GetCurrentProcess().Id) continue;
                            bool cerrado = false;
                            if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) cerrado = p.WaitForExit(4000);
                            if (!cerrado) { p.Kill(); p.WaitForExit(3000); }
                            cerrados++;
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                if (cerrados > 0)
                {
                    hecho.Add("Cerrado: " + f.Nombre);
                    if (f.Reabrir && !string.IsNullOrEmpty(f.Ruta)) estado.AppendLine("cerrado\t" + f.Nombre + "\t" + f.Ruta);
                    Util.Log("Modo juego: cerrado " + f.Nombre);
                }
            }

            File.WriteAllText(ArchivoEstado, estado.ToString(), Encoding.UTF8);
            Util.Log("Modo juego activado");
            return hecho;
        }

        /// <summary>Desactiva el modo juego y lo deja todo como estaba antes.</summary>
        public static List<string> Desactivar()
        {
            List<string> hecho = new List<string>();
            foreach (string linea in LeerEstado())
            {
                string[] p = linea.Split('\t');
                try
                {
                    if (p[0] == "plan" && p.Length > 1)
                    {
                        Util.EjecutarProceso(Util.RutaSistema("powercfg.exe"), "/setactive " + p[1], 20000);
                        hecho.Add("Plan de energía como estaba");
                    }
                    else if (p[0] == "juego" && p.Length > 1)
                    {
                        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClaveJuego))
                        {
                            if (p[1] == "nada") k.DeleteValue("AutoGameModeEnabled", false);
                            else k.SetValue("AutoGameModeEnabled", int.Parse(p[1]), RegistryValueKind.DWord);
                        }
                    }
                    else if (p[0] == "cerrado" && p.Length > 2 && File.Exists(p[2]))
                    {
                        // A través del Explorador, para que se abra con permisos normales y no como administrador
                        Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + p[2] + "\"");
                        hecho.Add("Abierto de nuevo: " + p[1]);
                    }
                }
                catch { }
            }
            try { File.Delete(ArchivoEstado); } catch { }
            Util.Log("Modo juego desactivado");
            return hecho;
        }

        private static List<string> LeerEstado()
        {
            List<string> l = new List<string>();
            try
            {
                if (File.Exists(ArchivoEstado))
                {
                    foreach (string s in File.ReadAllLines(ArchivoEstado, Encoding.UTF8)) if (s.Trim().Length > 0) l.Add(s.TrimEnd('\r'));
                }
            }
            catch { }
            return l;
        }

        private static string PlanActual()
        {
            ResultadoProceso r = Util.EjecutarProceso(Util.RutaSistema("powercfg.exe"), "/getactivescheme", 20000);
            Match m = Regex.Match(r.Salida, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value : null;
        }

        /// <summary>Nombre del plan de energía activo (para mostrarlo).</summary>
        public static string NombrePlanActual()
        {
            ResultadoProceso r = Util.EjecutarProceso(Util.RutaSistema("powercfg.exe"), "/getactivescheme", 20000);
            Match m = Regex.Match(r.Salida, @"\(([^)]+)\)");
            return m.Success ? m.Groups[1].Value : "desconocido";
        }
    }
}
