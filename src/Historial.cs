using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OptimizadorPC
{
    /// <summary>Una vez que la app liberó espacio.</summary>
    class EntradaHistorial
    {
        public DateTime Fecha;
        public string Origen;    // "Limpieza", "Limpieza automática", "Restos de programa"...
        public string Detalle;   // qué se borró
        public long Bytes;
    }

    /// <summary>
    /// Historial de todo el espacio que ha liberado la app. Se guarda en
    /// %LOCALAPPDATA%\OptimizadorPC\historial.txt (una línea por limpieza, separada por tabuladores).
    /// </summary>
    static class Historial
    {
        private static readonly object candado = new object();

        /// <summary>Quién está limpiando ahora (la tarea semanal lo cambia a "Limpieza automática").</summary>
        public static string Origen = "Limpieza";

        /// <summary>Solo para las pruebas automáticas: otro archivo, para no mezclar sus limpiezas con las reales.</summary>
        internal static string ArchivoPrueba;

        public static string Archivo { get { return ArchivoPrueba ?? Path.Combine(Util.CarpetaDatos, "historial.txt"); } }

        public static void Anotar(string origen, string detalle, long bytes)
        {
            if (bytes <= 0) return;
            string linea = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" +
                           Limpio(origen) + "\t" + Limpio(detalle) + "\t" + bytes.ToString(CultureInfo.InvariantCulture);
            try
            {
                lock (candado) File.AppendAllText(Archivo, linea + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static string Limpio(string s)
        {
            return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        /// <summary>Todas las entradas, de la más nueva a la más antigua.</summary>
        public static List<EntradaHistorial> Leer()
        {
            List<EntradaHistorial> l = new List<EntradaHistorial>();
            string[] lineas;
            try
            {
                lock (candado)
                {
                    if (!File.Exists(Archivo)) return l;
                    lineas = File.ReadAllLines(Archivo, Encoding.UTF8);
                }
            }
            catch { return l; }
            foreach (string linea in lineas)
            {
                string[] p = linea.Split('\t');
                if (p.Length < 4) continue;
                EntradaHistorial e = new EntradaHistorial();
                long b;
                if (!DateTime.TryParseExact(p[0].Trim('﻿'), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out e.Fecha)) continue;
                if (!long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) continue;
                e.Origen = p[1];
                e.Detalle = p[2];
                e.Bytes = b;
                l.Add(e);
            }
            l.Sort(delegate(EntradaHistorial a, EntradaHistorial c) { return c.Fecha.CompareTo(a.Fecha); });
            return l;
        }

        public static void Borrar()
        {
            try { lock (candado) File.Delete(Archivo); } catch { }
        }
    }
}
