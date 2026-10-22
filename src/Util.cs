using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace OptimizadorPC
{
    class ResultadoProceso
    {
        public int Codigo = -1;
        public string Salida = "";
        public string Error = "";
        public bool Ok { get { return Codigo == 0; } }
    }

    /// <summary>Utilidades generales: tamaños, registro, recorrido seguro de carpetas y ejecución de procesos.</summary>
    static class Util
    {
        private static readonly object candadoLog = new object();
        public static readonly bool EsAdmin = CalcularAdmin();

        private static bool CalcularAdmin()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ registro

        public static string CarpetaDatos
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizadorPC");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string ArchivoLog { get { return Path.Combine(CarpetaDatos, "registro.log"); } }

        public static void Log(string mensaje)
        {
            try
            {
                lock (candadoLog)
                {
                    File.AppendAllText(ArchivoLog, string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, mensaje, Environment.NewLine), Encoding.UTF8);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ texto

        public static string FormatoTamano(double bytes)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            if (bytes < 0) return "—";
            if (bytes >= 1024.0 * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("N2", es) + " GB";
            if (bytes >= 1024.0 * 1024) return (bytes / (1024.0 * 1024)).ToString("N1", es) + " MB";
            if (bytes >= 1024.0) return (bytes / 1024.0).ToString("N0", es) + " KB";
            return bytes.ToString("N0", es) + " B";
        }

        public static string Recortar(string texto, int maximo)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            if (texto.Length <= maximo) return texto;
            return texto.Substring(0, Math.Max(0, maximo - 1)) + "…";
        }

        /// <summary>Escapa un texto para meterlo entre comillas simples en PowerShell.</summary>
        public static string ComillasPS(string s)
        {
            return "'" + (s ?? "").Replace("'", "''") + "'";
        }

        // ------------------------------------------------------------------ rutas seguras

        private static string Normalizar(string ruta)
        {
            try { return Path.GetFullPath(ruta).TrimEnd('\\').ToLowerInvariant(); }
            catch { return (ruta ?? "").TrimEnd('\\').ToLowerInvariant(); }
        }

        /// <summary>
        /// Carpetas que el optimizador nunca debe vaciar aunque una ruta se resuelva mal
        /// (raíz del disco, Windows, perfiles de usuario, Archivos de programa...).
        /// </summary>
        public static bool EsRutaProtegida(string ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return true;
            if (ruta.Length < 4 || ruta[1] != ':' || ruta[2] != '\\') return true;
            string r = Normalizar(ruta);
            if (r.Length <= 3) return true;

            List<string> prohibidas = new List<string>();
            Environment.SpecialFolder[] especiales =
            {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.Programs,
                Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.Startup
            };
            foreach (Environment.SpecialFolder sf in especiales)
            {
                string p = Environment.GetFolderPath(sf);
                if (!string.IsNullOrEmpty(p)) prohibidas.Add(Normalizar(p));
            }
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(perfil))
            {
                string usuarios = Path.GetDirectoryName(perfil);
                if (!string.IsNullOrEmpty(usuarios)) prohibidas.Add(Normalizar(usuarios));
                prohibidas.Add(Normalizar(Path.Combine(perfil, "Downloads")));
                prohibidas.Add(Normalizar(Path.Combine(perfil, "AppData")));
            }
            return prohibidas.Contains(r);
        }

        /// <summary>Archivos de OneDrive que solo están en la nube (no ocupan disco).</summary>
        public static bool EsNube(FileAttributes a)
        {
            const int offline = 0x1000, recallOnOpen = 0x40000, recallOnDataAccess = 0x400000;
            return ((int)a & (offline | recallOnOpen | recallOnDataAccess)) != 0;
        }

        private static bool EsEnlace(string dir)
        {
            try { return (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        /// <summary>
        /// Expande una ruta absoluta que puede tener comodines * en sus carpetas
        /// (p. ej. "...\User Data\*\Cache"). Solo devuelve rutas que existen y no son enlaces.
        /// </summary>
        public static List<string> ExpandirRuta(string patron)
        {
            List<string> resultado = new List<string>();
            if (string.IsNullOrEmpty(patron) || patron.Length < 4 || patron[1] != ':' || patron[2] != '\\') return resultado;

            if (patron.IndexOf('*') < 0 && patron.IndexOf('?') < 0)
            {
                if (File.Exists(patron) || (Directory.Exists(patron) && !EsEnlace(patron))) resultado.Add(patron);
                return resultado;
            }

            string[] partes = patron.Split('\\');
            List<string> actuales = new List<string>();
            actuales.Add(partes[0] + "\\");
            for (int i = 1; i < partes.Length; i++)
            {
                string parte = partes[i];
                if (parte.Length == 0) continue;
                List<string> siguientes = new List<string>();
                foreach (string actual in actuales)
                {
                    if (parte.IndexOf('*') >= 0 || parte.IndexOf('?') >= 0)
                    {
                        try
                        {
                            foreach (string d in Directory.GetDirectories(actual, parte))
                            {
                                if (!EsEnlace(d)) siguientes.Add(d);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        string c = Path.Combine(actual, parte);
                        bool ultimo = i == partes.Length - 1;
                        if ((Directory.Exists(c) && !EsEnlace(c)) || (ultimo && File.Exists(c))) siguientes.Add(c);
                    }
                }
                actuales = siguientes;
                if (actuales.Count == 0) break;
            }
            resultado.AddRange(actuales);
            return resultado;
        }

        /// <summary>
        /// Recorre todos los archivos bajo una carpeta SIN seguir enlaces ni junctions
        /// (así nunca se sale de la carpeta) y saltando los archivos que solo están en la nube.
        /// Si 'raiz' es un archivo, devuelve solo ese archivo.
        /// </summary>
        public static IEnumerable<FileInfo> EnumerarArchivos(string raiz, CancellationToken ct)
        {
            if (File.Exists(raiz))
            {
                FileInfo unico = null;
                try { unico = new FileInfo(raiz); if (EsNube(unico.Attributes)) unico = null; } catch { unico = null; }
                if (unico != null) yield return unico;
                yield break;
            }
            if (!Directory.Exists(raiz)) yield break;

            Stack<string> pila = new Stack<string>();
            pila.Push(raiz);
            while (pila.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = pila.Pop();
                FileSystemInfo[] elementos;
                try { elementos = new DirectoryInfo(dir).GetFileSystemInfos(); }
                catch { continue; }

                foreach (FileSystemInfo i in elementos)
                {
                    FileAttributes a;
                    try { a = i.Attributes; } catch { continue; }
                    if ((a & FileAttributes.Directory) != 0)
                    {
                        if ((a & FileAttributes.ReparsePoint) == 0) pila.Push(i.FullName);
                    }
                    else if (!EsNube(a))
                    {
                        FileInfo f = i as FileInfo;
                        if (f != null) yield return f;
                    }
                }
            }
        }

        public static long TamanoCarpeta(string ruta, CancellationToken ct)
        {
            long total = 0;
            foreach (FileInfo f in EnumerarArchivos(ruta, ct))
            {
                try { total += f.Length; } catch { }
            }
            return total;
        }

        /// <summary>Borra un archivo (quitando solo-lectura). Devuelve false si está en uso o protegido.</summary>
        public static bool BorrarArchivo(FileInfo f)
        {
            try
            {
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                f.Delete();
                return true;
            }
            catch { return false; }
        }

        /// <summary>Borra las subcarpetas vacías de 'raiz' (nunca la propia raíz ni carpetas con contenido).</summary>
        public static void BorrarCarpetasVacias(string raiz)
        {
            if (!Directory.Exists(raiz) || EsRutaProtegida(raiz)) return;
            List<string> carpetas = new List<string>();
            Stack<string> pila = new Stack<string>();
            pila.Push(raiz);
            while (pila.Count > 0)
            {
                string d = pila.Pop();
                string[] hijos;
                try { hijos = Directory.GetDirectories(d); } catch { continue; }
                foreach (string h in hijos)
                {
                    if (EsEnlace(h)) continue;
                    carpetas.Add(h);
                    pila.Push(h);
                }
            }
            // Las subcarpetas siempre tienen rutas más largas que sus padres: se intentan primero
            carpetas.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string d in carpetas)
            {
                try { Directory.Delete(d, false); } catch { }
            }
        }

        // ------------------------------------------------------------------ procesos

        public static string RutaSistema(string exe)
        {
            return Path.Combine(Environment.SystemDirectory, exe);
        }

        private static Encoding CodificacionConsola()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.Default; }
        }

        /// <summary>Ejecuta un programa sin ventana y espera a que termine (bloquea: llamar dentro de Task.Run).</summary>
        public static ResultadoProceso EjecutarProceso(string exe, string argumentos, int timeoutMs)
        {
            return EjecutarProceso(exe, argumentos, timeoutMs, CodificacionConsola(), null);
        }

        /// <summary>
        /// Igual que EjecutarProceso pero avisa de cada línea de salida mientras se ejecuta.
        /// OJO: 'alLinea' se llama desde otro hilo; para tocar la interfaz usa BeginInvoke.
        /// </summary>
        public static ResultadoProceso EjecutarProceso(string exe, string argumentos, int timeoutMs, Encoding codificacion, Action<string> alLinea)
        {
            ResultadoProceso r = new ResultadoProceso();
            StringBuilder salida = new StringBuilder();
            StringBuilder error = new StringBuilder();
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = exe;
                    p.StartInfo.Arguments = argumentos ?? "";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.StandardOutputEncoding = codificacion;
                    p.StartInfo.StandardErrorEncoding = codificacion;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (salida) salida.AppendLine(e.Data);
                        if (alLinea != null) { try { alLinea(e.Data); } catch { } }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (error) error.AppendLine(e.Data);
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        r.Codigo = -1;
                        lock (error) error.AppendLine("Se agotó el tiempo de espera.");
                    }
                    else
                    {
                        p.WaitForExit();
                        r.Codigo = p.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                r.Codigo = -1;
                error.AppendLine(ex.Message);
            }
            lock (salida) r.Salida = salida.ToString();
            lock (error) r.Error = error.ToString();
            return r;
        }

        /// <summary>
        /// Ejecuta un script de Windows PowerShell oculto. Dentro del script existe la función
        /// L, que escribe una línea con sus argumentos separados por tabuladores:
        ///     Get-Process | ForEach-Object { L $_.Name $_.Id }
        /// Bloquea: llamar dentro de Task.Run.
        /// </summary>
        public static ResultadoProceso EjecutarPowerShell(string script, int timeoutMs)
        {
            string prefijo =
                "$ProgressPreference='SilentlyContinue'\n" +
                "$ErrorActionPreference='SilentlyContinue'\n" +
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8\n" +
                "function L { ($args | ForEach-Object { ([string]$_) -replace '[\\t\\r\\n]+',' ' }) -join \"`t\" }\n";
            string codificado = Convert.ToBase64String(Encoding.Unicode.GetBytes(prefijo + script));
            string ps = RutaSistema(@"WindowsPowerShell\v1.0\powershell.exe");
            return EjecutarProceso(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + codificado,
                timeoutMs, new UTF8Encoding(false), null);
        }

        /// <summary>Ejecuta PowerShell y devuelve cada línea no vacía partida por tabuladores (ver función L).</summary>
        public static List<string[]> PowerShellFilas(string script, int timeoutMs)
        {
            List<string[]> filas = new List<string[]>();
            ResultadoProceso r = EjecutarPowerShell(script, timeoutMs);
            foreach (string linea in r.Salida.Split('\n'))
            {
                string l = linea.TrimEnd('\r');
                if (l.Trim().Length == 0) continue;
                filas.Add(l.Split('\t'));
            }
            return filas;
        }

        /// <summary>Abre el Explorador con el archivo seleccionado.</summary>
        public static void AbrirUbicacion(string ruta)
        {
            try { Process.Start("explorer.exe", "/select,\"" + ruta + "\""); } catch { }
        }

        /// <summary>Abre un archivo, carpeta, programa o enlace con el programa predeterminado de Windows.</summary>
        public static bool Abrir(string objetivo, string argumentos)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(objetivo);
                psi.UseShellExecute = true;
                if (!string.IsNullOrEmpty(argumentos)) psi.Arguments = argumentos;
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }
    }
}
