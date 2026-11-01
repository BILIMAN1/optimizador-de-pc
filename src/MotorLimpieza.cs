using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace OptimizadorPC
{
    enum TipoEspecial { Ninguno, Papelera }

    /// <summary>Una categoría de archivos basura que se puede medir y limpiar.</summary>
    class Objetivo
    {
        public string Id;
        public string Nombre;
        public string Descripcion;
        public string[] Rutas = new string[0];
        public bool RequiereAdmin;
        public bool Recomendado;
        /// <summary>Solo se borran archivos con más de estos días (por fecha de creación Y de modificación).</summary>
        public int DiasMin;
        /// <summary>Si no es null, solo se borran archivos cuyo nombre encaje (p. ej. "thumbcache_*.db").</summary>
        public string[] Filtros;
        /// <summary>Servicios de Windows que hay que parar mientras se limpia.</summary>
        public string[] Servicios;
        /// <summary>Procesos que bloquean estos archivos (p. ej. navegadores abiertos).</summary>
        public string[] ProcesosQueBloquean;
        public TipoEspecial Especial;

        /// <summary>Último tamaño medido en bytes (-1 = sin medir).</summary>
        public long Tamano = -1;

        public bool Disponible { get { return !RequiereAdmin || Util.EsAdmin; } }
    }

    /// <summary>Mide y limpia archivos basura. Todos los métodos bloquean: llámalos dentro de Task.Run.</summary>
    static class MotorLimpieza
    {
        public static List<Objetivo> ObtenerObjetivos()
        {
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string raiz = Sistema.UnidadSistema;
            string temp = Path.GetTempPath().TrimEnd('\\');

            List<Objetivo> l = new List<Objetivo>();

            l.Add(Nuevo("temp_usuario", "Archivos temporales", "Restos que dejan los programas (con más de 1 día)",
                false, true, new[] { temp }, 1));
            l.Add(Nuevo("temp_windows", "Temporales de Windows", "Temporales del sistema (con más de 1 día)",
                true, true, new[] { Path.Combine(win, "Temp") }, 1));

            Objetivo wu = Nuevo("windows_update", "Caché de Windows Update", "Actualizaciones ya descargadas e instaladas",
                true, true, new[] { Path.Combine(win, @"SoftwareDistribution\Download") }, 0);
            wu.Servicios = new[] { "wuauserv", "bits" };
            l.Add(wu);

            l.Add(Nuevo("errores", "Informes de errores", "Archivos que se crean cuando un programa o Windows se cuelga",
                false, true, new[]
                {
                    Path.Combine(lad, "CrashDumps"), Path.Combine(lad, @"Microsoft\Windows\WER"),
                    Path.Combine(win, "Minidump"), Path.Combine(win, "LiveKernelReports"), Path.Combine(win, "MEMORY.DMP"),
                    Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue")
                }, 0));

            Objetivo mini = Nuevo("miniaturas", "Caché de miniaturas", "Vistas previas de fotos y vídeos (se regeneran solas)",
                false, true, new[] { Path.Combine(lad, @"Microsoft\Windows\Explorer") }, 0);
            mini.Filtros = new[] { "thumbcache_*.db" };
            l.Add(mini);

            Objetivo nav = Nuevo("navegadores", "Caché de navegadores", "Chrome, Edge, Brave, Opera y Firefox. No borra contraseñas ni sesiones",
                false, false, new[]
                {
                    Path.Combine(lad, @"Google\Chrome\User Data\*\Cache"), Path.Combine(lad, @"Google\Chrome\User Data\*\Code Cache"),
                    Path.Combine(lad, @"Google\Chrome\User Data\*\GPUCache"),
                    Path.Combine(lad, @"Microsoft\Edge\User Data\*\Cache"), Path.Combine(lad, @"Microsoft\Edge\User Data\*\Code Cache"),
                    Path.Combine(lad, @"Microsoft\Edge\User Data\*\GPUCache"),
                    Path.Combine(lad, @"BraveSoftware\Brave-Browser\User Data\*\Cache"), Path.Combine(lad, @"BraveSoftware\Brave-Browser\User Data\*\Code Cache"),
                    Path.Combine(lad, @"Opera Software\Opera Stable\Cache"), Path.Combine(lad, @"Opera Software\Opera GX Stable\Cache"),
                    Path.Combine(ad, @"Opera Software\Opera Stable\Cache"), Path.Combine(ad, @"Opera Software\Opera GX Stable\Cache"),
                    Path.Combine(lad, @"Mozilla\Firefox\Profiles\*\cache2")
                }, 0);
            nav.ProcesosQueBloquean = new[] { "chrome", "msedge", "brave", "opera", "firefox" };
            l.Add(nav);

            Objetivo discord = Nuevo("discord", "Caché de Discord", "Imágenes y vídeos guardados por Discord (se vuelven a descargar)",
                false, false, new[]
                {
                    Path.Combine(ad, @"discord\Cache"), Path.Combine(ad, @"discord\Code Cache"), Path.Combine(ad, @"discord\GPUCache")
                }, 0);
            discord.ProcesosQueBloquean = new[] { "Discord" };
            l.Add(discord);

            l.Add(Nuevo("drivers", "Instaladores de drivers", "Restos de instalar drivers de NVIDIA y AMD",
                false, false, new[]
                {
                    Path.Combine(raiz, "NVIDIA"), Path.Combine(raiz, "AMD"), Path.Combine(pd, @"NVIDIA Corporation\Downloader")
                }, 0));

            Objetivo papelera = Nuevo("papelera", "Papelera de reciclaje", "Borra DEFINITIVAMENTE lo que hay en la papelera",
                false, false, new string[0], 0);
            papelera.Especial = TipoEspecial.Papelera;
            l.Add(papelera);

            return l;
        }

        private static Objetivo Nuevo(string id, string nombre, string descripcion, bool admin, bool recomendado, string[] rutas, int diasMin)
        {
            Objetivo o = new Objetivo();
            o.Id = id;
            o.Nombre = nombre;
            o.Descripcion = descripcion;
            o.RequiereAdmin = admin;
            o.Recomendado = recomendado;
            o.Rutas = rutas;
            o.DiasMin = diasMin;
            return o;
        }

        /// <summary>Carpetas/archivos reales que cubre un objetivo, ya expandidos y filtrados por seguridad.</summary>
        public static List<string> ResolverRutas(Objetivo o)
        {
            List<string> resultado = new List<string>();
            HashSet<string> vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string patron in o.Rutas)
            {
                foreach (string ruta in Util.ExpandirRuta(patron))
                {
                    if (Util.EsRutaProtegida(ruta)) continue;
                    if (vistas.Add(ruta)) resultado.Add(ruta);
                }
            }
            return resultado;
        }

        /// <summary>Archivos que se borrarían para este objetivo.</summary>
        public static IEnumerable<FileInfo> ArchivosDe(Objetivo o, CancellationToken ct)
        {
            DateTime limite = DateTime.Now.AddDays(-o.DiasMin);
            foreach (string raiz in ResolverRutas(o))
            {
                foreach (FileInfo f in Util.EnumerarArchivos(raiz, ct))
                {
                    if (o.DiasMin > 0)
                    {
                        DateTime creado, modificado;
                        try { creado = f.CreationTime; modificado = f.LastWriteTime; }
                        catch { continue; }
                        if (creado >= limite || modificado >= limite) continue;
                    }
                    if (o.Filtros != null && !EncajaFiltro(f.Name, o.Filtros)) continue;
                    yield return f;
                }
            }
        }

        private static bool EncajaFiltro(string nombre, string[] filtros)
        {
            foreach (string patron in filtros)
            {
                if (Comodin(nombre, patron)) return true;
            }
            return false;
        }

        /// <summary>Comparación sencilla con * y ? (sin distinguir mayúsculas).</summary>
        private static bool Comodin(string texto, string patron)
        {
            string regex = "^" + System.Text.RegularExpressions.Regex.Escape(patron).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(texto, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>Mide cuántos bytes se liberarían. Guarda el resultado en o.Tamano. -1 si requiere admin.</summary>
        public static long Medir(Objetivo o, CancellationToken ct)
        {
            long total;
            if (!o.Disponible)
            {
                total = -1;
            }
            else if (o.Especial == TipoEspecial.Papelera)
            {
                total = Math.Max(0, Sistema.TamanoPapelera());
            }
            else
            {
                total = 0;
                foreach (FileInfo f in ArchivosDe(o, ct))
                {
                    try { total += f.Length; } catch { }
                }
            }
            o.Tamano = total;
            return total;
        }

        /// <summary>¿Hay abierto algún programa que bloquee los archivos de este objetivo?</summary>
        public static List<string> ProgramasAbiertosQueBloquean(Objetivo o)
        {
            List<string> abiertos = new List<string>();
            if (o.ProcesosQueBloquean == null) return abiertos;
            foreach (string nombre in o.ProcesosQueBloquean)
            {
                Process[] ps = null;
                try
                {
                    ps = Process.GetProcessesByName(nombre);
                    if (ps.Length > 0) abiertos.Add(nombre);
                }
                catch { }
                finally
                {
                    if (ps != null) foreach (Process p in ps) p.Dispose();
                }
            }
            return abiertos;
        }

        /// <summary>
        /// Limpia un objetivo y devuelve los bytes liberados. Los archivos en uso se saltan.
        /// 'progreso' (opcional) recibe mensajes de estado desde el hilo de trabajo.
        /// </summary>
        public static long Limpiar(Objetivo o, CancellationToken ct, Action<string> progreso)
        {
            if (!o.Disponible) return 0;
            if (progreso != null) progreso("Limpiando " + o.Nombre.ToLowerInvariant() + "…");
            long liberado = 0;

            if (o.Especial == TipoEspecial.Papelera)
            {
                long antes = Math.Max(0, Sistema.TamanoPapelera());
                Sistema.VaciarPapelera();
                long despues = Math.Max(0, Sistema.TamanoPapelera());
                liberado = Math.Max(0, antes - despues);
                Util.Log(string.Format("Limpieza '{0}': {1}", o.Nombre, Util.FormatoTamano(liberado)));
                Historial.Anotar(Historial.Origen, o.Nombre, liberado);
                o.Tamano = despues;
                return liberado;
            }

            List<ServiceController> parados = new List<ServiceController>();
            try
            {
                if (o.Servicios != null)
                {
                    foreach (string nombre in o.Servicios)
                    {
                        try
                        {
                            ServiceController sc = new ServiceController(nombre);
                            if (sc.Status == ServiceControllerStatus.Running)
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                parados.Add(sc);
                            }
                            else
                            {
                                sc.Dispose();
                            }
                        }
                        catch { }
                    }
                }

                foreach (FileInfo f in ArchivosDe(o, ct))
                {
                    long tam;
                    try { tam = f.Length; } catch { tam = 0; }
                    if (Util.BorrarArchivo(f)) liberado += tam;
                }
                if (o.Filtros == null)
                {
                    foreach (string raiz in ResolverRutas(o))
                    {
                        if (Directory.Exists(raiz)) Util.BorrarCarpetasVacias(raiz);
                    }
                }
            }
            finally
            {
                foreach (ServiceController sc in parados)
                {
                    try { sc.Start(); } catch { }
                    sc.Dispose();
                }
            }

            Util.Log(string.Format("Limpieza '{0}': {1}", o.Nombre, Util.FormatoTamano(liberado)));
            Historial.Anotar(Historial.Origen, o.Nombre, liberado);
            o.Tamano = -1;
            return liberado;
        }

        /// <summary>Limpia todo lo recomendado (lo usa el botón "Optimizar ahora" y la tarea semanal).</summary>
        public static long LimpiezaRapida(CancellationToken ct, Action<string> progreso)
        {
            long total = 0;
            foreach (Objetivo o in ObtenerObjetivos())
            {
                if (!o.Recomendado || !o.Disponible) continue;
                ct.ThrowIfCancellationRequested();
                total += Limpiar(o, ct, progreso);
            }
            Util.Log("Limpieza rápida terminada: " + Util.FormatoTamano(total));
            return total;
        }
    }
}
