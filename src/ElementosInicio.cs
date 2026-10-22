using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace OptimizadorPC
{
    enum TipoInicio { Registro, Carpeta, Tarea }

    /// <summary>Un programa o tarea que se ejecuta solo al iniciar Windows.</summary>
    class ElementoInicio
    {
        public string Nombre;
        public string Comando;
        public string Origen;          // "Usuario", "Todos los usuarios" o "Tarea programada"
        public TipoInicio Tipo;
        public bool Habilitado;
        /// <summary>Driver, antivirus o anticheat: mejor no apagarlo.</summary>
        public bool MejorMantener;
        public bool RequiereAdmin;

        // Datos internos para poder cambiar el estado
        internal RegistryHive Colmena;
        internal string SubclaveAprobacion;   // "Run", "Run32" o "StartupFolder"
        internal string Valor;                // nombre del valor en el registro o del archivo en la carpeta Inicio
        internal string RutaTarea;            // ruta completa de la tarea programada
    }

    /// <summary>
    /// Lee y cambia los programas de inicio igual que el Administrador de tareas
    /// (claves StartupApproved), así que apagar algo nunca borra nada y se puede revertir.
    /// </summary>
    static class ElementosInicio
    {
        private const string Aprobacion = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        private const string ClaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static readonly string[] Mantener =
        {
            "SecurityHealth", "Windows Defender", "Realtek", "RtkAud", "igfx", "Synaptics", "ETDCtrl",
            "Waves", "Dolby", "Vanguard", "vgtray", "OptimizadorPC"
        };

        /// <summary>Bloquea: llamar dentro de Task.Run (las tareas programadas tardan un par de segundos).</summary>
        public static List<ElementoInicio> Obtener(bool incluirTareas)
        {
            List<ElementoInicio> l = new List<ElementoInicio>();
            LeerRun(l, RegistryHive.CurrentUser, RegistryView.Registry64, "Run", "Usuario");
            LeerRun(l, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", "Todos los usuarios");
            if (Environment.Is64BitOperatingSystem)
            {
                LeerRun(l, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", "Todos los usuarios");
            }
            LeerCarpeta(l, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Usuario");
            LeerCarpeta(l, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Todos los usuarios");
            if (incluirTareas) LeerTareas(l);

            foreach (ElementoInicio e in l)
            {
                string texto = (e.Nombre + " " + e.Comando).ToLowerInvariant();
                foreach (string m in Mantener)
                {
                    if (texto.Contains(m.ToLowerInvariant())) { e.MejorMantener = true; break; }
                }
            }
            return l;
        }

        private static void LeerRun(List<ElementoInicio> l, RegistryHive colmena, RegistryView vista, string subAprob, string origen)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, vista))
                using (RegistryKey k = b.OpenSubKey(ClaveRun))
                {
                    if (k == null) return;
                    foreach (string n in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        ElementoInicio e = new ElementoInicio();
                        e.Nombre = n;
                        e.Comando = Convert.ToString(k.GetValue(n));
                        e.Origen = origen;
                        e.Tipo = TipoInicio.Registro;
                        e.Colmena = colmena;
                        e.SubclaveAprobacion = subAprob;
                        e.Valor = n;
                        e.RequiereAdmin = colmena == RegistryHive.LocalMachine;
                        e.Habilitado = EstaAprobado(colmena, subAprob, n);
                        l.Add(e);
                    }
                }
            }
            catch { }
        }

        private static void LeerCarpeta(List<ElementoInicio> l, string carpeta, RegistryHive colmena, string origen)
        {
            if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) return;
            try
            {
                foreach (FileInfo f in new DirectoryInfo(carpeta).GetFiles())
                {
                    if (string.Equals(f.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    ElementoInicio e = new ElementoInicio();
                    e.Nombre = Path.GetFileNameWithoutExtension(f.Name);
                    e.Comando = f.FullName;
                    e.Origen = origen;
                    e.Tipo = TipoInicio.Carpeta;
                    e.Colmena = colmena;
                    e.SubclaveAprobacion = "StartupFolder";
                    e.Valor = f.Name;
                    e.RequiereAdmin = colmena == RegistryHive.LocalMachine;
                    e.Habilitado = EstaAprobado(colmena, "StartupFolder", f.Name);
                    l.Add(e);
                }
            }
            catch { }
        }

        private static void LeerTareas(List<ElementoInicio> l)
        {
            // Solo tareas de terceros: las de Microsoft (\Microsoft\...) no se tocan.
            const string script =
                "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' } | ForEach-Object {\n" +
                "  $c = ($_.Actions | ForEach-Object { ('{0} {1}' -f $_.Execute, $_.Arguments).Trim() }) -join ' | '\n" +
                "  L $_.TaskName $_.TaskPath $_.State $c\n" +
                "}";
            foreach (string[] f in Util.PowerShellFilas(script, 60000))
            {
                if (f.Length < 3 || string.IsNullOrEmpty(f[0])) continue;
                ElementoInicio e = new ElementoInicio();
                e.Nombre = f[0];
                e.RutaTarea = (f[1].EndsWith("\\") ? f[1] : f[1] + "\\") + f[0];
                e.Habilitado = !string.Equals(f[2], "Disabled", StringComparison.OrdinalIgnoreCase);
                e.Comando = f.Length > 3 ? f[3] : "";
                e.Origen = "Tarea programada";
                e.Tipo = TipoInicio.Tarea;
                e.RequiereAdmin = true;
                l.Add(e);
            }
        }

        // Windows guarda un byte par si está activo y uno impar si está apagado
        private static bool EstaAprobado(RegistryHive colmena, string subAprob, string valor)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(Aprobacion + subAprob))
                {
                    if (k == null) return true;
                    byte[] datos = k.GetValue(valor) as byte[];
                    if (datos == null || datos.Length == 0) return true;
                    return (datos[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        /// <summary>
        /// Activa o apaga un elemento. Lanza una excepción con un mensaje legible si no se pudo
        /// (normalmente por falta de permisos de administrador). Bloquea: llamar dentro de Task.Run.
        /// </summary>
        public static void CambiarEstado(ElementoInicio e, bool habilitar)
        {
            if (e.Tipo == TipoInicio.Tarea)
            {
                ResultadoProceso r = Util.EjecutarProceso(Util.RutaSistema("schtasks.exe"),
                    "/Change /TN \"" + e.RutaTarea + "\" " + (habilitar ? "/ENABLE" : "/DISABLE"), 30000);
                if (!r.Ok)
                {
                    string msg = (r.Error + " " + r.Salida).Trim();
                    throw new InvalidOperationException(msg.Length > 0 ? msg : "No se pudo cambiar la tarea programada.");
                }
            }
            else
            {
                byte[] datos = new byte[12];
                if (habilitar)
                {
                    datos[0] = 2;
                }
                else
                {
                    datos[0] = 3;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(datos, 4);
                }
                try
                {
                    using (RegistryKey b = RegistryKey.OpenBaseKey(e.Colmena, RegistryView.Registry64))
                    using (RegistryKey k = b.CreateSubKey(Aprobacion + e.SubclaveAprobacion))
                    {
                        if (k == null) throw new InvalidOperationException("No se pudo abrir el registro.");
                        k.SetValue(e.Valor, datos, RegistryValueKind.Binary);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("Hacen falta permisos de administrador para cambiar este programa.");
                }
                catch (System.Security.SecurityException)
                {
                    throw new InvalidOperationException("Hacen falta permisos de administrador para cambiar este programa.");
                }
            }
            e.Habilitado = habilitar;
            Util.Log(string.Format("Inicio: '{0}' ({1}) -> {2}", e.Nombre, e.Tipo, habilitar ? "activado" : "apagado"));
        }
    }
}
