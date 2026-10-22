using System;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;

namespace OptimizadorPC
{
    /// <summary>
    /// Sonidos de la app, generados por código según el tema (no hay archivos de audio):
    /// campanita (normal), fantasma y risa de bruja (Halloween), caja de música desafinada (weirdcore).
    /// </summary>
    static class Sonidos
    {
        private const int Hz = 22050;

        private static string ArchivoAjuste { get { return Path.Combine(Util.CarpetaDatos, "sonidos.txt"); } }

        /// <summary>Si están activados (por defecto sí).</summary>
        public static bool Activados
        {
            get { try { return !File.Exists(ArchivoAjuste) || File.ReadAllText(ArchivoAjuste).Trim() != "no"; } catch { return true; } }
            set { try { File.WriteAllText(ArchivoAjuste, value ? "si" : "no"); } catch { } }
        }

        public static void Exito() { Reproducir(Tema.Weirdcore ? CajaDeMusica() : Tema.Halloween ? Bruja() : Campanita()); }
        public static void Error() { Reproducir(Tema.Weirdcore ? Distorsion(Fallo(), 0.6f) : Fallo()); }
        public static void Secreto() { Reproducir(Susurro()); }

        /// <summary>Suena aunque estén desactivados (para el botón "Probar").</summary>
        public static void Probar() { Reproducir(Tema.Weirdcore ? CajaDeMusica() : Tema.Halloween ? Bruja() : Campanita(), true); }

        private static void Reproducir(float[] muestras) { Reproducir(muestras, false); }

        private static void Reproducir(float[] muestras, bool siempre)
        {
            // En las pruebas automáticas (diálogos con respuesta automática) nunca suena nada
            if ((!siempre && !Activados) || Dialogo.RespuestaAutomatica != null) return;
            byte[] wav = Wav(muestras);
            Thread hilo = new Thread(delegate()
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream(wav))
                    using (SoundPlayer sp = new SoundPlayer(ms))
                    {
                        sp.PlaySync();
                    }
                }
                catch { }
            });
            hilo.IsBackground = true;
            hilo.Start();
        }

        // ------------------------------------------------------------------ sonidos

        private static float[] Campanita()
        {
            float[] s = Nuevo(0.9);
            Nota(s, 0.00, 0.6, 1046.5, 0.5f, 6);
            Nota(s, 0.12, 0.7, 1568.0, 0.45f, 5);
            return s;
        }

        private static float[] Bruja()
        {
            float[] s = Nuevo(1.55);
            // "Uuuuh" de fantasma: tono que baja con vibrato
            for (int i = 0; i < Muestras(0.6); i++)
            {
                double t = i / (double)Hz;
                double f = 680 - 260 * (t / 0.6) + 14 * Math.Sin(2 * Math.PI * 6 * t);
                double env = Math.Min(1, t / 0.08) * Math.Min(1, (0.6 - t) / 0.15);
                s[i] += (float)(0.45 * env * Math.Sin(Fase(ref faseBruja, f)));
            }
            // "Je je je je je": pulsos cortos que bajan de tono, con algo de ruido
            Random r = new Random(3);
            for (int p = 0; p < 6; p++)
            {
                double inicio = 0.68 + p * 0.13, largo = 0.095, f0 = 1150 - p * 95;
                int a = Muestras(inicio), n = Muestras(largo);
                double fase = 0;
                for (int i = 0; i < n && a + i < s.Length; i++)
                {
                    double t = i / (double)Hz;
                    double env = Math.Sin(Math.PI * t / largo);
                    double f = f0 * (1 - 0.18 * t / largo);
                    fase += 2 * Math.PI * f / Hz;
                    double v = Math.Sin(fase) + 0.5 * Math.Sin(2 * fase) + 0.3 * Math.Sin(3 * fase) + 0.15 * (r.NextDouble() * 2 - 1);
                    s[a + i] += (float)(0.28 * env * v);
                }
            }
            return s;
        }
        private static double faseBruja;

        private static float[] CajaDeMusica()
        {
            float[] s = Nuevo(1.3);
            // Arpegio un poco desafinado y con "temblor", como una cinta vieja
            double[] notas = { 659.3 * 1.012, 784.0 * 0.988, 987.8 * 1.02, 1318.5 * 0.985, 987.8 * 1.01 };
            for (int k = 0; k < notas.Length; k++)
            {
                int a = Muestras(k * 0.16);
                double fase = 0;
                for (int i = 0; i < Muestras(0.7) && a + i < s.Length; i++)
                {
                    double t = i / (double)Hz;
                    double f = notas[k] * (1 + 0.012 * Math.Sin(2 * Math.PI * 3.3 * (t + k)));
                    fase += 2 * Math.PI * f / Hz;
                    double env = Math.Exp(-t * 5) * Math.Min(1, t / 0.005);
                    s[a + i] += (float)(0.33 * env * (Math.Sin(fase) + 0.25 * Math.Sin(3 * fase)));
                }
            }
            return Distorsion(s, 0.45f);
        }

        private static float[] Fallo()
        {
            float[] s = Nuevo(0.42);
            Nota(s, 0.00, 0.18, 329.6, 0.35f, 9);
            Nota(s, 0.17, 0.25, 220.0, 0.35f, 8);
            return s;
        }

        private static float[] Susurro()
        {
            float[] s = Nuevo(2.2);
            Random r = new Random(11);
            double[] acorde = { 110.0, 138.6 * 1.01, 164.8 * 0.99, 207.7 * 1.015 };
            double[] fases = new double[acorde.Length];
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)Hz;
                double env = Math.Min(1, t / 1.2) * Math.Min(1, (2.2 - t) / 0.6);
                double v = 0;
                for (int k = 0; k < acorde.Length; k++)
                {
                    fases[k] += 2 * Math.PI * acorde[k] * (1 - 0.05 * t / 2.2) / Hz;
                    v += Math.Sin(fases[k]);
                }
                double ruido = (r.NextDouble() * 2 - 1) * 0.35 * Math.Min(1, t / 1.6);
                // Pequeños "cortes" de señal, como una grabación dañada
                double corte = (i / Muestras(0.07)) % 7 == 3 ? 0.2 : 1;
                s[i] = (float)(0.22 * env * corte * (v / acorde.Length * 2 + ruido));
            }
            return Distorsion(s, 0.35f);
        }

        // ------------------------------------------------------------------ utilidades de audio

        private static float[] Nuevo(double segundos) { return new float[Muestras(segundos)]; }
        private static int Muestras(double segundos) { return (int)(segundos * Hz); }

        private static double Fase(ref double fase, double f)
        {
            fase += 2 * Math.PI * f / Hz;
            return fase;
        }

        private static void Nota(float[] s, double inicio, double largo, double f, float volumen, double caida)
        {
            int a = Muestras(inicio);
            for (int i = 0; i < Muestras(largo) && a + i < s.Length; i++)
            {
                double t = i / (double)Hz;
                double env = Math.Exp(-t * caida) * Math.Min(1, t / 0.004);
                double v = Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(4 * Math.PI * f * t);
                s[a + i] += (float)(volumen * env * v);
            }
        }

        /// <summary>Mezcla el sonido con una versión de pocos bits (sonido "roto", de aparato viejo).</summary>
        private static float[] Distorsion(float[] s, float cantidad)
        {
            for (int i = 0; i < s.Length; i++)
            {
                float roto = (float)Math.Round(s[i] * 6) / 6f;
                s[i] = s[i] * (1 - cantidad) + roto * cantidad;
            }
            return s;
        }

        private static byte[] Wav(float[] s)
        {
            float pico = 0.0001f;
            foreach (float v in s) pico = Math.Max(pico, Math.Abs(v));
            float escala = 0.8f / pico;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int datos = s.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + datos); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Hz); w.Write(Hz * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(datos);
                foreach (float v in s) w.Write((short)Math.Max(-32767, Math.Min(32767, v * escala * 32767)));
                return ms.ToArray();
            }
        }
    }
}
