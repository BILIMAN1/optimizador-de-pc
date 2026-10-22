using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace OptimizadorPC
{
    class Arranque
    {
        public DateTime Fecha;
        public double Segundos;          // hasta que el escritorio está listo
        public double SegundosDespues;   // lo que tarda en terminar de cargar todo detrás
    }

    class CausaLenta
    {
        public string Nombre, Tipo;      // Tipo: Programa, Controlador, Servicio
        public int Veces;
        public double RetrasoMax;        // segundos de retraso que causó
    }

    class InfoArranque
    {
        public List<Arranque> Arranques = new List<Arranque>();
        public List<CausaLenta> Causas = new List<CausaLenta>();
        public bool SinPermiso;
    }

    class Controlador
    {
        public string Dispositivo, Tipo, Fabricante, Version;
        public DateTime? Fecha;
    }

    class InfoActualizaciones
    {
        public DateTime? UltimaInstalada;
        public bool ReinicioPendiente;
        public List<string> Pendientes;   // null = aún no se ha buscado
        public string Error;
    }

    /// <summary>Diagnóstico de solo lectura: arranque, actualizaciones y controladores. Bloquea: llamar dentro de Task.Run.</summary>
    static class Diagnostico
    {
        private const string Registro = "Microsoft-Windows-Diagnostics-Performance/Operational";

        /// <summary>
        /// Lee el registro de rendimiento de Windows: cuánto tardaron los últimos arranques y qué programas,
        /// controladores o servicios los hicieron más lentos (últimos 90 días). Necesita administrador.
        /// </summary>
        public static InfoArranque LeerArranque()
        {
            InfoArranque info = new InfoArranque();
            string script =
                "function D($e) { $x = [xml]$e.ToXml(); $h = @{}; foreach ($d in $x.Event.EventData.Data) { $h[$d.Name] = $d.'#text' }; $h }\n" +
                "try {\n" +
                "  Get-WinEvent -FilterHashtable @{ LogName = '" + Registro + "'; Id = 100 } -MaxEvents 12 -ErrorAction Stop | ForEach-Object {\n" +
                "    $h = D $_; L 'boot' $_.TimeCreated.ToString('yyyy-MM-dd HH:mm') $h['MainPathBootTime'] $h['BootPostBootTime']\n" +
                "  }\n" +
                "} catch { if ($_.Exception -is [UnauthorizedAccessException] -or $_.Exception.Message -match 'acceso|access') { L 'sinpermiso' } }\n" +
                "Get-WinEvent -FilterHashtable @{ LogName = '" + Registro + "'; Id = 101, 102, 103; StartTime = (Get-Date).AddDays(-90) } -ErrorAction SilentlyContinue | ForEach-Object {\n" +
                "  $h = D $_; $n = $h['FriendlyName']; if (-not $n) { $n = $h['Name'] }\n" +
                "  L 'lento' $_.Id $n $h['DegradationTime']\n" +
                "}\n";
            Dictionary<string, CausaLenta> causas = new Dictionary<string, CausaLenta>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] f in Util.PowerShellFilas(script, 120000))
            {
                try
                {
                    if (f[0] == "sinpermiso") info.SinPermiso = true;
                    else if (f[0] == "boot" && f.Length >= 4)
                    {
                        Arranque a = new Arranque();
                        a.Fecha = DateTime.ParseExact(f[1], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        a.Segundos = Ms(f[2]) / 1000.0;
                        a.SegundosDespues = Ms(f[3]) / 1000.0;
                        if (a.Segundos > 0) info.Arranques.Add(a);
                    }
                    else if (f[0] == "lento" && f.Length >= 4 && f[2].Trim().Length > 0)
                    {
                        string tipo = f[1] == "101" ? "Programa" : f[1] == "102" ? "Controlador" : "Servicio";
                        string clave = tipo + "|" + f[2].Trim();
                        CausaLenta c;
                        if (!causas.TryGetValue(clave, out c))
                        {
                            c = new CausaLenta();
                            c.Nombre = f[2].Trim();
                            c.Tipo = tipo;
                            causas[clave] = c;
                        }
                        c.Veces++;
                        c.RetrasoMax = Math.Max(c.RetrasoMax, Ms(f[3]) / 1000.0);
                    }
                }
                catch { }
            }
            info.Arranques.Sort(delegate(Arranque a, Arranque b) { return a.Fecha.CompareTo(b.Fecha); });
            info.Causas.AddRange(causas.Values);
            info.Causas.Sort(delegate(CausaLenta a, CausaLenta b) { return b.RetrasoMax.CompareTo(a.RetrasoMax); });
            return info;
        }

        private static double Ms(string s)
        {
            double v;
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        /// <summary>Controladores de la gráfica, el sonido, la red y el Bluetooth, con su fecha.</summary>
        public static List<Controlador> LeerControladores()
        {
            List<Controlador> l = new List<Controlador>();
            const string script =
                "Get-CimInstance Win32_PnPSignedDriver -Filter \"DeviceClass='DISPLAY' OR DeviceClass='MEDIA' OR DeviceClass='NET' OR DeviceClass='BLUETOOTH'\" |\n" +
                "  Where-Object { $_.DeviceName -and $_.DriverVersion } | ForEach-Object {\n" +
                "    $f = ''; if ($_.DriverDate) { $f = $_.DriverDate.ToString('yyyy-MM-dd') }\n" +
                "    L $_.DeviceClass $_.DeviceName $_.Manufacturer $_.DriverVersion $f\n" +
                "  }";
            HashSet<string> vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] f in Util.PowerShellFilas(script, 90000))
            {
                if (f.Length < 5 || !vistos.Add(f[1] + "|" + f[3])) continue;
                // Adaptadores virtuales y de software: no interesan
                string n = f[1].ToLowerInvariant();
                if (n.Contains("wan miniport") || n.Contains("virtual") || n.Contains("kernel debug") || n.Contains("teredo") ||
                    n.Contains("microsoft wi-fi direct") || n.Contains("bluetooth device (personal") || n.Contains("rfcomm")) continue;
                Controlador c = new Controlador();
                c.Tipo = f[0] == "DISPLAY" ? "Gráfica" : f[0] == "MEDIA" ? "Sonido" : f[0] == "NET" ? "Red" : "Bluetooth";
                c.Dispositivo = f[1];
                c.Fabricante = f[2];
                c.Version = f[3];
                DateTime d;
                if (DateTime.TryParseExact(f[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) c.Fecha = d;
                l.Add(c);
            }
            string[] orden = { "Gráfica", "Sonido", "Red", "Bluetooth" };
            l.Sort(delegate(Controlador a, Controlador b)
            {
                int t = Array.IndexOf(orden, a.Tipo).CompareTo(Array.IndexOf(orden, b.Tipo));
                return t != 0 ? t : string.Compare(a.Dispositivo, b.Dispositivo, StringComparison.CurrentCultureIgnoreCase);
            });
            return l;
        }

        /// <summary>Lo rápido: última actualización instalada y si falta reiniciar.</summary>
        public static InfoActualizaciones LeerEstadoActualizaciones()
        {
            InfoActualizaciones i = new InfoActualizaciones();
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                {
                    i.ReinicioPendiente = k != null;
                }
            }
            catch { }
            foreach (string[] f in Util.PowerShellFilas(
                "$h = Get-HotFix | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending | Select-Object -First 1\n" +
                "if ($h) { L $h.InstalledOn.ToString('yyyy-MM-dd') }", 60000))
            {
                DateTime d;
                if (f.Length > 0 && DateTime.TryParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) i.UltimaInstalada = d;
            }
            return i;
        }

        /// <summary>Pregunta a Windows Update qué actualizaciones faltan (puede tardar un par de minutos). No instala nada.</summary>
        public static List<string> BuscarActualizaciones(out string error)
        {
            error = null;
            List<string> l = new List<string>();
            const string script =
                "try {\n" +
                "  $s = New-Object -ComObject Microsoft.Update.Session\n" +
                "  $r = $s.CreateUpdateSearcher().Search(\"IsInstalled=0 and IsHidden=0\")\n" +
                "  foreach ($u in $r.Updates) { L 'upd' $u.Title }\n" +
                "  L 'fin'\n" +
                "} catch { L 'error' $_.Exception.Message }";
            bool fin = false;
            foreach (string[] f in Util.PowerShellFilas(script, 300000))
            {
                if (f[0] == "upd" && f.Length > 1) l.Add(f[1]);
                else if (f[0] == "fin") fin = true;
                else if (f[0] == "error") error = f.Length > 1 ? f[1] : "error desconocido";
            }
            if (!fin && error == null) error = "Windows Update no respondió a tiempo";
            return l;
        }
    }
}
