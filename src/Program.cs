using System;
using System.Threading;
using System.Windows.Forms;

namespace OptimizadorPC
{
    static class Program
    {
        /// <summary>
        /// Argumentos:
        ///   /limpiezarapida            limpia lo recomendado sin ventana (lo usa la tarea semanal)
        ///   /pagina id                 abre directamente esa sección
        ///   /captura carpeta [/espera ms]   pruebas: guarda una captura PNG de cada página y se cierra
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            string captura = null, pagina = null, temaForzado = null;
            int espera = 6000;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/limpiezarapida") return LimpiezaRapida();
                if (a == "/icono" && i + 1 < args.Length) return GenerarIcono(args[i + 1]);
                if (a == "/captura" && i + 1 < args.Length) captura = args[++i];
                else if (a == "/pagina" && i + 1 < args.Length) pagina = args[++i].ToLowerInvariant();
                else if (a == "/espera" && i + 1 < args.Length) int.TryParse(args[++i], out espera);
                else if (a == "/tema" && i + 1 < args.Length) temaForzado = args[++i].ToLowerInvariant();
            }

            bool unica;
            using (Mutex m = new Mutex(true, "OptimizadorPC_instancia_unica", out unica))
            {
                if (!unica && captura == null)
                {
                    MessageBox.Show("El Optimizador de PC ya está abierto.", "Optimizador de PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { ReportarError(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Util.Log("ERROR FATAL: " + e.ExceptionObject);
                };

                Tema.Inicializar();
                Tema.Aplicar(temaForzado ?? Tema.LeerPreferencia());
                Util.Log("App abierta" + (Util.EsAdmin ? " (administrador)" : " (sin administrador)"));
                while (true)
                {
                    MainForm f = new MainForm(pagina, captura, espera, captura != null && pagina != null);
                    Application.Run(f);
                    if (f.TemaSiguiente == null) break;
                    // Se cambió de tema: se guarda y se vuelve a abrir la ventana en la misma página
                    Tema.Aplicar(f.TemaSiguiente);
                    Tema.GuardarPreferencia(Tema.Nombre);
                    Util.Log("Tema cambiado a " + Tema.Nombre);
                    pagina = f.PaginaActual;
                }
            }
            return 0;
        }

        private static int LimpiezaRapida()
        {
            try
            {
                Util.Log("Inicio de la limpieza semanal automática");
                Historial.Origen = "Limpieza automática semanal";
                MotorLimpieza.LimpiezaRapida(CancellationToken.None, null);
                return 0;
            }
            catch (Exception ex)
            {
                Util.Log("Error en la limpieza automática: " + ex);
                return 1;
            }
        }

        // Solo para compilar: guarda el logo como .ico (PNG de 16 a 256 px dentro de un ICO)
        private static int GenerarIcono(string ruta)
        {
            int[] tamanos = { 16, 24, 32, 48, 64, 128, 256 };
            System.Collections.Generic.List<byte[]> pngs = new System.Collections.Generic.List<byte[]>();
            foreach (int t in tamanos)
            {
                using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(t, t))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(b))
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    int m = Math.Max(0, t / 32);
                    Tema.DibujarLogo(g, new System.Drawing.Rectangle(m, m, t - 2 * m - 1, t - 2 * m - 1));
                    b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            }
            using (System.IO.BinaryWriter w = new System.IO.BinaryWriter(System.IO.File.Create(ruta)))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)tamanos.Length);
                int desplazamiento = 6 + 16 * tamanos.Length;
                for (int i = 0; i < tamanos.Length; i++)
                {
                    byte lado = (byte)(tamanos[i] >= 256 ? 0 : tamanos[i]);
                    w.Write(lado); w.Write(lado); w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32); w.Write(pngs[i].Length); w.Write(desplazamiento);
                    desplazamiento += pngs[i].Length;
                }
                foreach (byte[] png in pngs) w.Write(png);
            }
            return 0;
        }

        internal static void ReportarError(Exception ex)
        {
            if (ex == null) return;
            Util.Log("ERROR: " + ex);
            try
            {
                Dialogo.Error(null, "Algo salió mal", ex.Message + "\n\nEl detalle se guardó en el registro de la app.");
            }
            catch { }
        }
    }
}
