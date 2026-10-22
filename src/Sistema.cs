using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OptimizadorPC
{
    class InfoDisco
    {
        public string Unidad;
        public long Total;
        public long Libre;
        public long Usado { get { return Total - Libre; } }
        public double Porcentaje { get { return Total <= 0 ? 0 : Usado * 100.0 / Total; } }
    }

    class InfoMemoria
    {
        public long Total;
        public long Disponible;
        public long Usada { get { return Total - Disponible; } }
        public double Porcentaje { get { return Total <= 0 ? 0 : Usada * 100.0 / Total; } }
    }

    /// <summary>Información del sistema: disco, RAM, tiempo encendido, papelera y carpetas conocidas.</summary>
    static class Sistema
    {
        /// <summary>Raíz del disco de Windows, normalmente "C:\".</summary>
        public static string UnidadSistema
        {
            get
            {
                string r = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                return string.IsNullOrEmpty(r) ? @"C:\" : r;
            }
        }

        /// <summary>"C:" sin barra.</summary>
        public static string LetraSistema { get { return UnidadSistema.TrimEnd('\\'); } }

        public static InfoDisco Disco()
        {
            InfoDisco d = new InfoDisco();
            d.Unidad = LetraSistema;
            try
            {
                DriveInfo di = new DriveInfo(UnidadSistema);
                d.Total = di.TotalSize;
                d.Libre = di.AvailableFreeSpace;
            }
            catch { }
            return d;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        public static InfoMemoria Memoria()
        {
            InfoMemoria m = new InfoMemoria();
            try
            {
                MEMORYSTATUSEX s = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(s))
                {
                    m.Total = (long)s.ullTotalPhys;
                    m.Disponible = (long)s.ullAvailPhys;
                }
            }
            catch { }
            return m;
        }

        public static TimeSpan TiempoEncendido()
        {
            try { return TimeSpan.FromMilliseconds(GetTickCount64()); }
            catch { return TimeSpan.Zero; }
        }

        // ---- Papelera de reciclaje (solo la del disco del sistema) ----

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct SHQUERYRBINFO64 { public int cbSize; public long i64Size; public long i64NumItems; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SHQUERYRBINFO32 { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        private static extern int SHQueryRecycleBin64(string raiz, ref SHQUERYRBINFO64 info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        private static extern int SHQueryRecycleBin32(string raiz, ref SHQUERYRBINFO32 info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string raiz, uint flags);

        /// <summary>Bytes que ocupa la papelera del disco del sistema (-1 si no se pudo saber).</summary>
        public static long TamanoPapelera()
        {
            try
            {
                if (IntPtr.Size == 8)
                {
                    SHQUERYRBINFO64 i = new SHQUERYRBINFO64();
                    i.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO64));
                    return SHQueryRecycleBin64(UnidadSistema, ref i) == 0 ? i.i64Size : -1;
                }
                else
                {
                    SHQUERYRBINFO32 i = new SHQUERYRBINFO32();
                    i.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO32));
                    return SHQueryRecycleBin32(UnidadSistema, ref i) == 0 ? i.i64Size : -1;
                }
            }
            catch { return -1; }
        }

        /// <summary>Vacía la papelera del disco del sistema sin preguntar. Es definitivo.</summary>
        public static bool VaciarPapelera()
        {
            const uint SIN_CONFIRMAR = 0x1, SIN_PROGRESO = 0x2, SIN_SONIDO = 0x4;
            try
            {
                int hr = SHEmptyRecycleBin(IntPtr.Zero, UnidadSistema, SIN_CONFIRMAR | SIN_PROGRESO | SIN_SONIDO);
                return hr == 0 || TamanoPapelera() == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Tamaño de un archivo en la raíz del disco del sistema (hiberfil.sys, pagefile.sys...).
        /// Esos archivos están bloqueados, así que se leen listando la carpeta. -1 si no existe.
        /// </summary>
        public static long TamanoArchivoRaiz(string nombre)
        {
            try
            {
                foreach (FileInfo f in new DirectoryInfo(UnidadSistema).GetFiles(nombre))
                {
                    if (string.Equals(f.Name, nombre, StringComparison.OrdinalIgnoreCase)) return f.Length;
                }
            }
            catch { }
            return -1;
        }

        // ---- Carpetas conocidas ----

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr ruta);

        public static string CarpetaConocida(Guid id)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out p) == 0) return Marshal.PtrToStringUni(p);
            }
            catch { }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
            }
            return null;
        }

        public static string CarpetaDescargas
        {
            get
            {
                string d = CarpetaConocida(new Guid("374DE290-123F-4565-9164-39C4925E467B"));
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                return d;
            }
        }
    }
}
