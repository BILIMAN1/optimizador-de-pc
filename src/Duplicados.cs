using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace OptimizadorPC
{
    /// <summary>Un grupo de archivos con exactamente el mismo contenido.</summary>
    class GrupoDuplicado
    {
        public long Tamano;
        public List<FileInfo> Archivos = new List<FileInfo>();
        /// <summary>Espacio que se libera dejando solo una copia.</summary>
        public long Sobrante { get { return Tamano * (Archivos.Count - 1); } }
    }

    /// <summary>
    /// Busca archivos repetidos en las carpetas personales. Primero agrupa por tamaño, luego compara
    /// el principio y el final del archivo y, por último, el contenido completo (huella SHA-256), así
    /// que dos archivos solo se consideran iguales si lo son byte a byte. Solo lee: no borra nada.
    /// </summary>
    static class Duplicados
    {
        // Carpetas de programación y cachés: llenas de copias que no se deben tocar
        private static readonly string[] Ignorar =
        {
            @"\node_modules\", @"\.git\", @"\site-packages\", @"\__pycache__\", @"\.venv\", @"\venv\", @"\bin\Debug\",
            @"\obj\", @"\.cache\", @"\.nuget\", @"\.gradle\", @"\.m2\", @"\AppData\", @"\.vscode\", @"\.android\"
        };

        /// <summary>Carpetas donde se busca: las del perfil del usuario menos AppData y las ocultas.</summary>
        public static List<string> CarpetasDeBusqueda()
        {
            List<string> l = new List<string>();
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            try
            {
                foreach (DirectoryInfo d in new DirectoryInfo(perfil).GetDirectories())
                {
                    if (d.Name.StartsWith(".") || string.Equals(d.Name, "AppData", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((d.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    l.Add(d.FullName);
                }
            }
            catch { }
            return l;
        }

        public static List<GrupoDuplicado> Buscar(List<string> carpetas, long minimo, CancellationToken ct, Action<string> progreso)
        {
            // 1) Por tamaño
            Dictionary<long, List<FileInfo>> porTamano = new Dictionary<long, List<FileInfo>>();
            HashSet<string> vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string c in carpetas)
            {
                if (progreso != null) progreso("Revisando " + Path.GetFileName(c) + "…");
                foreach (FileInfo f in Util.EnumerarArchivos(c, ct))
                {
                    long tam;
                    try { tam = f.Length; } catch { continue; }
                    // Las carpetas a ignorar se buscan solo DENTRO de la carpeta revisada
                    string relativa = f.FullName.Length > c.Length ? f.FullName.Substring(c.TrimEnd('\\').Length) : f.FullName;
                    if (tam < minimo || !vistos.Add(f.FullName) || EsIgnorado(relativa)) continue;
                    List<FileInfo> l;
                    if (!porTamano.TryGetValue(tam, out l)) { l = new List<FileInfo>(); porTamano[tam] = l; }
                    l.Add(f);
                }
            }

            // 2) Mismo principio y final, 3) mismo contenido completo
            List<GrupoDuplicado> grupos = new List<GrupoDuplicado>();
            int revisados = 0, total = 0;
            foreach (List<FileInfo> l in porTamano.Values) if (l.Count > 1) total += l.Count;
            foreach (KeyValuePair<long, List<FileInfo>> par in porTamano)
            {
                if (par.Value.Count < 2) continue;
                ct.ThrowIfCancellationRequested();
                foreach (List<FileInfo> rapidos in Agrupar(par.Value, delegate(FileInfo f) { return HuellaRapida(f); }))
                {
                    foreach (List<FileInfo> iguales in Agrupar(rapidos, delegate(FileInfo f) { ct.ThrowIfCancellationRequested(); return HuellaCompleta(f); }))
                    {
                        GrupoDuplicado g = new GrupoDuplicado();
                        g.Tamano = par.Key;
                        g.Archivos = iguales;
                        // La copia más antigua va primero (es la que se propone conservar)
                        g.Archivos.Sort(delegate(FileInfo a, FileInfo b) { return Fecha(a).CompareTo(Fecha(b)); });
                        grupos.Add(g);
                    }
                }
                revisados += par.Value.Count;
                if (progreso != null) progreso("Comparando contenido… " + revisados + " de " + total);
            }
            grupos.Sort(delegate(GrupoDuplicado a, GrupoDuplicado b) { return b.Sobrante.CompareTo(a.Sobrante); });
            return grupos;
        }

        private static bool EsIgnorado(string ruta)
        {
            foreach (string i in Ignorar) if (ruta.IndexOf(i, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static DateTime Fecha(FileInfo f)
        {
            try { return f.CreationTime < f.LastWriteTime ? f.CreationTime : f.LastWriteTime; } catch { return DateTime.MaxValue; }
        }

        private static List<List<FileInfo>> Agrupar(List<FileInfo> archivos, Func<FileInfo, string> huella)
        {
            Dictionary<string, List<FileInfo>> d = new Dictionary<string, List<FileInfo>>();
            foreach (FileInfo f in archivos)
            {
                string h = huella(f);
                if (h == null) continue;   // no se pudo leer: nunca cuenta como duplicado
                List<FileInfo> l;
                if (!d.TryGetValue(h, out l)) { l = new List<FileInfo>(); d[h] = l; }
                l.Add(f);
            }
            List<List<FileInfo>> r = new List<List<FileInfo>>();
            foreach (List<FileInfo> l in d.Values) if (l.Count > 1) r.Add(l);
            return r;
        }

        // Primeros y últimos 64 KB: descarta muy rápido los que solo coinciden en tamaño
        private static string HuellaRapida(FileInfo f)
        {
            try
            {
                using (FileStream fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buf = new byte[65536];
                    int n = fs.Read(buf, 0, buf.Length);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    if (fs.Length > buf.Length * 2)
                    {
                        fs.Seek(-buf.Length, SeekOrigin.End);
                        n = fs.Read(buf, 0, buf.Length);
                    }
                    sha.TransformFinalBlock(buf, 0, n);
                    return Convert.ToBase64String(sha.Hash);
                }
            }
            catch { return null; }
        }

        public static string HuellaCompleta(FileInfo f)
        {
            try
            {
                using (FileStream fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
                using (SHA256 sha = SHA256.Create())
                {
                    return Convert.ToBase64String(sha.ComputeHash(fs));
                }
            }
            catch { return null; }
        }
    }
}
