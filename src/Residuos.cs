using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace OptimizadorPC
{
    enum TipoResto { Carpeta, Archivo, AccesoDirecto, Registro }

    /// <summary>Algo que un programa dejó atrás al desinstalarse.</summary>
    class Resto
    {
        public TipoResto Tipo;
        /// <summary>Ruta del archivo/carpeta, o la clave del registro como "HKCU\Software\X".</summary>
        public string Ruta;
        public long Tamano = -1;
        public string Motivo;
        public bool Marcado = true;

        /// <summary>Tras borrar: true si se borró, y el texto para el informe ("A la papelera", "No se pudo: en uso"...).</summary>
        public bool Borrado;
        public string Resultado;

        internal RegistryHive Colmena;
        internal RegistryView Vista;
        internal string Subclave;
    }

    /// <summary>Lo que sabemos de un programa ANTES de desinstalarlo (luego su entrada desaparece).</summary>
    class DatosPrograma
    {
        public string Nombre = "", Editor = "", CarpetaInstalacion = "", Desinstalador = "", Icono = "";
        internal RegistryHive Colmena;
        internal RegistryView Vista;
        internal string Subclave;
    }

    class ResultadoBorradoRestos
    {
        public int Borrados, Fallidos;
        public long Bytes;
        public string CarpetaRespaldo;
        public List<string> Errores = new List<string>();
    }

    /// <summary>
    /// Busca y borra los restos de un programa desinstalado (carpetas, registro y accesos directos),
    /// como hacen Geek Uninstaller o Revo. Es conservador: solo propone cosas cuyo nombre coincide
    /// exactamente con el programa y nunca nada que use otro programa instalado.
    /// Todos los métodos bloquean: llamar dentro de Task.Run.
    /// </summary>
    static class Residuos
    {
        private const string ClaveDesinstalar = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        // Nombres demasiado genéricos: nunca se borra una carpeta o clave que se llame así
        private static readonly HashSet<string> Genericos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft", "microsoft corporation", "windows", "common files", "programs", "program files", "program files (x86)",
            "programdata", "google", "google llc", "intel", "intel corporation", "nvidia", "nvidia corporation", "amd",
            "advanced micro devices", "apple", "adobe", "temp", "tmp", "cache", "caches", "packages", "update", "updater",
            "installer", "installers", "setup", "uninstall", "bin", "app", "apps", "data", "local", "locallow", "roaming",
            "x64", "x86", "steam", "valve", "epic games", "riot games", "default", "users", "public", "documents",
            "downloads", "desktop", "drivers", "system32", "syswow64", "crashdumps", "logs", "log", "config", "settings",
            "software", "classes", "policies", "wow6432node", "clients", "mozilla", "oracle", "java", "python",
            "realtek", "dell", "hp", "lenovo", "asus", "acer", "msi", "razer", "logitech", "corsair", "steelseries",
            "start menu", "startup", "accessories", "administrative tools", "system tools", "maintenance",
            "games", "tools", "utilities", "help", "support", "plugins", "extensions", "shared", "common", "temp files",
            "inc", "llc", "ltd", "corporation", "software inc", "the", "and", "for", "free", "pro", "plus", "beta"
        };

        // ------------------------------------------------------------------ antes de desinstalar

        /// <summary>Lee la información del programa antes de desinstalarlo. null si ya no está.</summary>
        public static DatosPrograma Leer(RegistryHive colmena, RegistryView vista, string subclave)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, vista))
                using (RegistryKey k = b.OpenSubKey(ClaveDesinstalar + "\\" + subclave))
                {
                    if (k == null) return null;
                    DatosPrograma d = new DatosPrograma();
                    d.Nombre = Texto(k, "DisplayName");
                    d.Editor = Texto(k, "Publisher");
                    d.CarpetaInstalacion = Texto(k, "InstallLocation").Trim('"', ' ').TrimEnd('\\');
                    d.Desinstalador = Texto(k, "UninstallString");
                    d.Icono = Texto(k, "DisplayIcon");
                    d.Colmena = colmena;
                    d.Vista = vista;
                    d.Subclave = subclave;
                    return d;
                }
            }
            catch { return null; }
        }

        private static string Texto(RegistryKey k, string valor)
        {
            try
            {
                object o = k.GetValue(valor);
                return o == null ? "" : Environment.ExpandEnvironmentVariables(Convert.ToString(o)).Trim();
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ nombres

        /// <summary>"Ollama version 0.40.6" -> "Ollama"; "Microsoft Visual Studio Code (User)" -> "Microsoft Visual Studio Code".</summary>
        public static string NombreLimpio(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return "";
            string s = Regex.Replace(nombre, @"\([^)]*\)|\[[^\]]*\]", " ");
            s = Regex.Replace(s, @"\b(versi[oó]n|version|ver\.?|v)\s*\d[\w.\-]*", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b\d+(\.\d+)+[\w\-]*", " ");
            s = Regex.Replace(s, @"\b(x64|x86|64[\s\-]?bit|32[\s\-]?bit|amd64|arm64)\b", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+-\s+.*$", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_', '.', ',');
            return s;
        }

        /// <summary>"Riot Games, Inc." -> "Riot Games".</summary>
        public static string EditorLimpio(string editor)
        {
            if (string.IsNullOrEmpty(editor)) return "";
            string s = Regex.Replace(editor, @"[,.]?\s*\b(inc|llc|ltd|limited|corp|corporation|co|gmbh|s\.?a\.?|s\.?l\.?|ag|bv|pty|plc|srl)\b\.?", " ", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\s+", " ").Trim(' ', ',', '.', '-');
        }

        private static bool NombreValido(string n)
        {
            if (string.IsNullOrEmpty(n) || n.Length < 3) return false;
            if (Genericos.Contains(n)) return false;
            if (Regex.IsMatch(n, @"^[\d\W_]+$")) return false;
            return n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // Carpeta que contiene un .exe sacado de UninstallString/DisplayIcon
        private static string CarpetaDeExe(string comando)
        {
            if (string.IsNullOrEmpty(comando)) return null;
            string c = comando.Trim();
            string exe = null;
            if (c.StartsWith("\""))
            {
                int fin = c.IndexOf('"', 1);
                if (fin > 1) exe = c.Substring(1, fin - 1);
            }
            else
            {
                Match m = Regex.Match(c, @"^(.+?\.exe)\b", RegexOptions.IgnoreCase);
                if (m.Success) exe = m.Groups[1].Value;
                else exe = c.Split(',')[0];
            }
            if (string.IsNullOrEmpty(exe) || exe.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            try
            {
                exe = exe.Split(',')[0].Trim();
                if (!Path.IsPathRooted(exe)) return null;
                string d = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(d)) return null;
                // Muchos desinstaladores viven en una subcarpeta (uninst, _uninstaller...): se sube un nivel
                string hoja = Path.GetFileName(d).ToLowerInvariant();
                if (hoja.Contains("uninst") || hoja == "bin" || hoja == "x64" || hoja == "x86") d = Path.GetDirectoryName(d);
                return d;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ otros programas (para no tocarlos)

        private class OtroPrograma { public string Nombre, Limpio, Carpeta; }

        private static List<OtroPrograma> OtrosProgramas(DatosPrograma excluir)
        {
            List<OtroPrograma> l = new List<OtroPrograma>();
            List<KeyValuePair<RegistryHive, RegistryView>> ramas = new List<KeyValuePair<RegistryHive, RegistryView>>();
            ramas.Add(new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry64));
            if (Environment.Is64BitOperatingSystem) ramas.Add(new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry32));
            ramas.Add(new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Registry64));
            foreach (KeyValuePair<RegistryHive, RegistryView> r in ramas)
            {
                try
                {
                    using (RegistryKey b = RegistryKey.OpenBaseKey(r.Key, r.Value))
                    using (RegistryKey raiz = b.OpenSubKey(ClaveDesinstalar))
                    {
                        if (raiz == null) continue;
                        foreach (string sub in raiz.GetSubKeyNames())
                        {
                            if (excluir != null && r.Key == excluir.Colmena && r.Value == excluir.Vista &&
                                string.Equals(sub, excluir.Subclave, StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                using (RegistryKey k = raiz.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    string nombre = Texto(k, "DisplayName");
                                    if (nombre.Length == 0) continue;
                                    if (excluir != null && string.Equals(nombre, excluir.Nombre, StringComparison.OrdinalIgnoreCase)) continue;
                                    OtroPrograma o = new OtroPrograma();
                                    o.Nombre = nombre;
                                    o.Limpio = NombreLimpio(nombre);
                                    string carpeta = Texto(k, "InstallLocation").Trim('"', ' ').TrimEnd('\\');
                                    if (carpeta.Length == 0) carpeta = CarpetaDeExe(Texto(k, "DisplayIcon")) ?? CarpetaDeExe(Texto(k, "UninstallString")) ?? "";
                                    o.Carpeta = carpeta;
                                    l.Add(o);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return l;
        }

        private static bool MismaRutaODentro(string ruta, string otra)
        {
            if (string.IsNullOrEmpty(ruta) || string.IsNullOrEmpty(otra)) return false;
            string a = ruta.TrimEnd('\\') + "\\", b = otra.TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ búsqueda

        /// <summary>
        /// Busca restos del programa. Con incluirEntrada=true también propone borrar su entrada de
        /// "Programas instalados" (para cuando el desinstalador ya no existe).
        /// </summary>
        public static List<Resto> Buscar(DatosPrograma p, bool incluirEntrada, CancellationToken ct)
        {
            List<Resto> restos = new List<Resto>();
            if (p == null) return restos;
            List<OtroPrograma> otros = OtrosProgramas(p);

            // Nombres que identifican al programa
            List<string> nombres = new List<string>();
            AgregarNombre(nombres, NombreLimpio(p.Nombre));
            AgregarNombre(nombres, p.Nombre);
            string carpetaInst = p.CarpetaInstalacion;
            if (string.IsNullOrEmpty(carpetaInst) || Util.EsRutaProtegida(carpetaInst))
            {
                carpetaInst = CarpetaDeExe(p.Icono) ?? CarpetaDeExe(p.Desinstalador);
            }
            if (!string.IsNullOrEmpty(carpetaInst) && !Util.EsRutaProtegida(carpetaInst))
            {
                AgregarNombre(nombres, Path.GetFileName(carpetaInst.TrimEnd('\\')));
            }
            // Quitar nombres que también identifican a otro programa instalado (p. ej. "Riot" con LoL y Valorant)
            nombres.RemoveAll(delegate(string n)
            {
                foreach (OtroPrograma o in otros)
                {
                    if (string.Equals(o.Limpio, n, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(o.Nombre, n, StringComparison.OrdinalIgnoreCase)) return true;
                    if (!string.IsNullOrEmpty(o.Carpeta) && string.Equals(Path.GetFileName(o.Carpeta), n, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            });

            List<string> editores = new List<string>();
            string ed = EditorLimpio(p.Editor);
            if (NombreValido(ed)) editores.Add(ed);
            if (NombreValido(p.Editor) && !editores.Contains(p.Editor)) editores.Add(p.Editor);

            // 1) La carpeta de instalación, si sigue ahí
            if (!string.IsNullOrEmpty(carpetaInst) && Directory.Exists(carpetaInst))
            {
                AgregarCarpeta(restos, otros, carpetaInst, "Carpeta donde estaba instalado", ct);
                // Si el nombre de la carpeta no se parece al del programa, se propone pero sin marcar
                Resto inst = restos.Find(x => string.Equals(x.Ruta, carpetaInst.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                if (inst != null && !Relacionado(Path.GetFileName(inst.Ruta), p.Nombre, p.Editor))
                {
                    inst.Marcado = false;
                    inst.Motivo = "Carpeta donde estaba instalado (revísala antes de borrar)";
                }
            }

            // 2) Carpetas con el nombre del programa en los sitios habituales
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] raices =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                lad,
                Path.Combine(lad, "Programs"),
                string.IsNullOrEmpty(perfil) ? null : Path.Combine(perfil, @"AppData\LocalLow"),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };
            foreach (string raiz in raices)
            {
                if (string.IsNullOrEmpty(raiz) || !Directory.Exists(raiz)) continue;
                foreach (string n in nombres)
                {
                    ct.ThrowIfCancellationRequested();
                    AgregarCarpeta(restos, otros, Path.Combine(raiz, n), "Carpeta con el nombre del programa", ct);
                    foreach (string e in editores)
                    {
                        AgregarCarpeta(restos, otros, Path.Combine(Path.Combine(raiz, e), n), "Carpeta de " + e, ct);
                    }
                }
            }

            // 3) Accesos directos rotos o que apuntan a la carpeta del programa
            string[] carpetasAccesos =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };
            foreach (string c in carpetasAccesos)
            {
                BuscarAccesos(restos, c, nombres, carpetaInst);
            }

            // 4) Claves del registro con el nombre del programa
            foreach (string n in nombres)
            {
                AgregarClave(restos, RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\" + n);
                AgregarClave(restos, RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\" + n);
                if (Environment.Is64BitOperatingSystem) AgregarClave(restos, RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\" + n);
                foreach (string e in editores)
                {
                    AgregarClave(restos, RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\" + e + "\\" + n);
                    AgregarClave(restos, RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\" + e + "\\" + n);
                    if (Environment.Is64BitOperatingSystem) AgregarClave(restos, RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\" + e + "\\" + n);
                }
            }

            // 5) Su entrada en "Programas instalados", si sigue ahí sin desinstalador
            if (!string.IsNullOrEmpty(p.Subclave))
            {
                bool existe = false;
                try
                {
                    using (RegistryKey b = RegistryKey.OpenBaseKey(p.Colmena, p.Vista))
                    using (RegistryKey k = b.OpenSubKey(ClaveDesinstalar + "\\" + p.Subclave))
                    {
                        existe = k != null;
                    }
                }
                catch { }
                if (existe && incluirEntrada)
                {
                    Resto r = NuevaClave(p.Colmena, p.Vista, ClaveDesinstalar + "\\" + p.Subclave);
                    r.Motivo = "Entrada en la lista de programas instalados";
                    restos.Add(r);
                }
            }

            // Quitar duplicados y cosas que ya están dentro de otra que se va a borrar
            List<Resto> final = new List<Resto>();
            foreach (Resto r in restos)
            {
                bool dentro = false;
                foreach (Resto o in restos)
                {
                    if (o == r || o.Tipo != r.Tipo) continue;
                    string a = r.Ruta.TrimEnd('\\') + "\\", b = o.Ruta.TrimEnd('\\') + "\\";
                    if (a.Length > b.Length && a.StartsWith(b, StringComparison.OrdinalIgnoreCase)) { dentro = true; break; }
                }
                if (dentro) continue;
                if (final.Exists(x => x.Tipo == r.Tipo && string.Equals(x.Ruta, r.Ruta, StringComparison.OrdinalIgnoreCase))) continue;
                final.Add(r);
            }
            return final;
        }

        /// <summary>¿Comparte la carpeta alguna palabra de 4+ letras con el nombre o el editor del programa?</summary>
        private static bool Relacionado(string carpeta, string nombre, string editor)
        {
            string c = Regex.Replace(carpeta ?? "", @"[^\p{L}\p{N}]", "").ToLowerInvariant();
            if (c.Length == 0) return false;
            foreach (Match m in Regex.Matches((nombre ?? "") + " " + (editor ?? ""), @"[\p{L}\p{N}]{4,}"))
            {
                string palabra = m.Value.ToLowerInvariant();
                if (Genericos.Contains(palabra)) continue;
                if (c.Contains(palabra)) return true;
            }
            string junto = Regex.Replace(NombreLimpio(nombre), @"[^\p{L}\p{N}]", "").ToLowerInvariant();
            return junto.Length >= 4 && (c.Contains(junto) || junto.Contains(c) && c.Length >= 4);
        }

        private static void AgregarNombre(List<string> nombres, string n)
        {
            if (n == null) return;
            n = n.Trim();
            if (!NombreValido(n)) return;
            foreach (string x in nombres) if (string.Equals(x, n, StringComparison.OrdinalIgnoreCase)) return;
            nombres.Add(n);
        }

        private static void AgregarCarpeta(List<Resto> restos, List<OtroPrograma> otros, string ruta, string motivo, CancellationToken ct)
        {
            try
            {
                if (!Directory.Exists(ruta)) return;
                ruta = Path.GetFullPath(ruta).TrimEnd('\\');
                if (Util.EsRutaProtegida(ruta)) return;
                if (MismaRutaODentro(ruta, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) return;
                if ((File.GetAttributes(ruta) & FileAttributes.ReparsePoint) != 0) return;
                if (Genericos.Contains(Path.GetFileName(ruta))) return;
                // Nunca una carpeta que usa (o contiene) otro programa instalado
                foreach (OtroPrograma o in otros)
                {
                    if (!string.IsNullOrEmpty(o.Carpeta) && MismaRutaODentro(ruta, o.Carpeta)) return;
                }
                foreach (Resto r in restos) if (string.Equals(r.Ruta, ruta, StringComparison.OrdinalIgnoreCase)) return;
                Resto nuevo = new Resto();
                nuevo.Tipo = TipoResto.Carpeta;
                nuevo.Ruta = ruta;
                nuevo.Motivo = motivo;
                nuevo.Tamano = Util.TamanoCarpeta(ruta, ct);
                restos.Add(nuevo);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        private static void BuscarAccesos(List<Resto> restos, string carpeta, List<string> nombres, string carpetaInst)
        {
            if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) return;
            string[] lnks;
            try { lnks = Directory.GetFiles(carpeta, "*.lnk", SearchOption.TopDirectoryOnly); }
            catch { return; }
            foreach (string lnk in lnks)
            {
                string titulo = Path.GetFileNameWithoutExtension(lnk);
                bool porNombre = false;
                foreach (string n in nombres)
                {
                    if (string.Equals(titulo, n, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(NombreLimpio(titulo), n, StringComparison.OrdinalIgnoreCase)) { porNombre = true; break; }
                }
                string destino = DestinoAcceso(lnk);
                bool apuntaAlPrograma = !string.IsNullOrEmpty(destino) && !string.IsNullOrEmpty(carpetaInst) &&
                                        MismaRutaODentro(destino, carpetaInst) && destino.Length > carpetaInst.Length;
                bool roto = !string.IsNullOrEmpty(destino) && !File.Exists(destino) && !Directory.Exists(destino);
                // Solo si está roto (el programa ya no existe) o apunta dentro de la carpeta del programa
                if ((porNombre && roto) || apuntaAlPrograma)
                {
                    Resto r = new Resto();
                    r.Tipo = TipoResto.AccesoDirecto;
                    r.Ruta = lnk;
                    r.Motivo = roto ? "Acceso directo que ya no funciona" : "Acceso directo al programa";
                    try { r.Tamano = new FileInfo(lnk).Length; } catch { }
                    restos.Add(r);
                }
            }
        }

        /// <summary>A dónde apunta un acceso directo (.lnk). null si no se pudo leer.</summary>
        public static string DestinoAcceso(string lnk)
        {
            object shell = null, acceso = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);
                acceso = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                object destino = acceso.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, acceso, null);
                return destino as string;
            }
            catch { return null; }
            finally
            {
                if (acceso != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(acceso);
                if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static Resto NuevaClave(RegistryHive colmena, RegistryView vista, string subclave)
        {
            Resto r = new Resto();
            r.Tipo = TipoResto.Registro;
            r.Colmena = colmena;
            r.Vista = vista;
            r.Subclave = subclave;
            r.Ruta = (colmena == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") +
                     (vista == RegistryView.Registry32 && colmena == RegistryHive.LocalMachine ? subclave.Replace(@"SOFTWARE\", @"SOFTWARE\WOW6432Node\") : subclave);
            return r;
        }

        private static void AgregarClave(List<Resto> restos, RegistryHive colmena, RegistryView vista, string subclave)
        {
            try
            {
                string hoja = subclave.Substring(subclave.LastIndexOf('\\') + 1);
                if (!NombreValido(hoja)) return;
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, vista))
                using (RegistryKey k = b.OpenSubKey(subclave))
                {
                    if (k == null) return;
                }
                Resto r = NuevaClave(colmena, vista, subclave);
                r.Motivo = "Configuración guardada en el registro";
                foreach (Resto x in restos) if (x.Tipo == TipoResto.Registro && string.Equals(x.Ruta, r.Ruta, StringComparison.OrdinalIgnoreCase)) return;
                restos.Add(r);
            }
            catch { }
        }

        // ------------------------------------------------------------------ borrado

        /// <summary>
        /// Borra los restos indicados. Carpetas, archivos y accesos directos van a la papelera;
        /// de cada clave del registro se guarda antes una copia .reg (si la copia falla, no se borra).
        /// </summary>
        public static ResultadoBorradoRestos Borrar(List<Resto> restos, string programa = null)
        {
            ResultadoBorradoRestos res = new ResultadoBorradoRestos();
            string respaldo = Path.Combine(Path.Combine(Util.CarpetaDatos, "respaldos"), DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            int n = 0;
            foreach (Resto r in restos)
            {
                try
                {
                    if (r.Tipo == TipoResto.Registro)
                    {
                        Directory.CreateDirectory(respaldo);
                        res.CarpetaRespaldo = respaldo;
                        n++;
                        string archivo = Path.Combine(respaldo, n.ToString("00") + "_" + Regex.Replace(r.Ruta.Substring(r.Ruta.LastIndexOf('\\') + 1), @"[^\w\-]", "_") + ".reg");
                        string clave = (r.Colmena == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") + r.Subclave;
                        ResultadoProceso exp = Util.EjecutarProceso(Util.RutaSistema("reg.exe"),
                            "export \"" + clave + "\" \"" + archivo + "\" /y " + (r.Vista == RegistryView.Registry32 ? "/reg:32" : "/reg:64"), 60000);
                        if (!exp.Ok || !File.Exists(archivo)) throw new InvalidOperationException("no se pudo guardar la copia de seguridad");
                        using (RegistryKey b = RegistryKey.OpenBaseKey(r.Colmena, r.Vista))
                        {
                            b.DeleteSubKeyTree(r.Subclave, false);
                        }
                        Util.Log("Restos: borrada la clave " + r.Ruta + " (copia en " + archivo + ")");
                        r.Resultado = "Borrada (con copia de seguridad)";
                    }
                    else
                    {
                        if (Util.EsRutaProtegida(r.Ruta)) throw new InvalidOperationException("ruta protegida");
                        long tam = Math.Max(0, r.Tamano);
                        string padre = Path.GetDirectoryName(r.Ruta);
                        Exception error = MoverAPapelera(r.Ruta, r.Tipo == TipoResto.Carpeta);
                        if (error != null) throw error;
                        if (File.Exists(r.Ruta) || Directory.Exists(r.Ruta)) throw new InvalidOperationException("sigue en su sitio");
                        res.Bytes += tam;
                        Util.Log("Restos: a la papelera " + r.Ruta);
                        // Si la carpeta del editor se queda vacía, también se quita
                        BorrarPadreVacio(padre);
                        r.Resultado = "Enviado a la papelera";
                    }
                    r.Borrado = true;
                    res.Borrados++;
                }
                catch (Exception ex)
                {
                    res.Fallidos++;
                    r.Borrado = false;
                    r.Resultado = "No se pudo: " + MotivoLegible(ex);
                    res.Errores.Add(r.Ruta + ": " + ex.Message);
                    Util.Log("Restos: no se pudo borrar " + r.Ruta + ": " + ex.Message);
                }
            }
            Historial.Anotar("Restos de programa", string.IsNullOrEmpty(programa) ? "Restos" : programa, res.Bytes);
            return res;
        }

        private static string MotivoLegible(Exception ex)
        {
            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException) return "sin permiso (abre la app como administrador)";
            if (ex is IOException) return "está en uso (cierra el programa o reinicia y vuelve a intentarlo)";
            if (ex is OperationCanceledException) return "cancelado";
            return ex.Message;
        }

        private static void BorrarPadreVacio(string padre)
        {
            try
            {
                if (string.IsNullOrEmpty(padre) || Util.EsRutaProtegida(padre) || !Directory.Exists(padre)) return;
                if (Genericos.Contains(Path.GetFileName(padre))) return;
                string raizPadre = Path.GetDirectoryName(padre);
                if (string.IsNullOrEmpty(raizPadre) || !Util.EsRutaProtegida(raizPadre)) return;   // solo justo debajo de Archivos de programa, AppData...
                if (Directory.GetFileSystemEntries(padre).Length == 0) Directory.Delete(padre, false);
            }
            catch { }
        }

        /// <summary>Manda un archivo o carpeta a la papelera (en un hilo STA, como pide Windows). null si fue bien.</summary>
        public static Exception MoverAPapelera(string ruta, bool esCarpeta)
        {
            Exception error = null;
            Thread hilo = new Thread(delegate()
            {
                try
                {
                    if (esCarpeta)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(ruta,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    }
                    else
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(ruta,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    }
                }
                catch (Exception ex) { error = ex; }
            });
            hilo.SetApartmentState(ApartmentState.STA);
            hilo.IsBackground = true;
            hilo.Start();
            hilo.Join();
            return error;
        }
    }
}
