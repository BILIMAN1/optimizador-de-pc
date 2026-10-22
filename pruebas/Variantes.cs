using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>
    /// Herramienta de diseño (no forma parte de la app): pinta la app real con varias paletas
    /// weirdcore alternativas y monta una hoja comparativa. No cambia nada de la app ni del PC.
    /// </summary>
    static class Variantes
    {
        class Variante
        {
            public string Nombre, Idea;
            public Action Colores;
        }

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        static void Base(Color fondo, Color barra, Color tarjeta, Color borde, Color texto, Color suave, Color tenue,
                         Color acento, Color acentoTexto, Color decor, Color seleccion)
        {
            Tema.Fondo = fondo; Tema.Barra = barra; Tema.Tarjeta = tarjeta;
            Tema.TarjetaHover = Mezcla(tarjeta, texto, 0.06f); Tema.Control = Mezcla(tarjeta, texto, 0.10f);
            Tema.ControlHover = Mezcla(tarjeta, texto, 0.16f); Tema.Borde = borde;
            Tema.Texto = texto; Tema.TextoSuave = suave; Tema.TextoTenue = tenue;
            Tema.Acento = acento; Tema.AcentoHover = Mezcla(acento, Color.White, 0.25f); Tema.AcentoOscuro = Mezcla(acento, Color.Black, 0.2f);
            Tema.AcentoTexto = acentoTexto; Tema.Morado = decor; Tema.Seleccion = seleccion;
            Tema.ControlApagado = Mezcla(tarjeta, texto, 0.05f); Tema.NavSeleccion = Mezcla(barra, texto, 0.09f); Tema.NavHover = Mezcla(barra, texto, 0.05f);
            Tema.CajaFondo = Tema.ControlApagado; Tema.InterruptorApagado = Mezcla(tarjeta, texto, 0.22f);
            Tema.InterruptorApagadoHover = Mezcla(tarjeta, texto, 0.3f); Tema.FilaAlterna = Mezcla(tarjeta, texto, 0.025f);
        }

        static Color Mezcla(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static readonly Variante[] Lista =
        {
            new Variante { Nombre = "1. Weirdcore (el actual)", Idea = "Azul de pantalla vieja y rosa chicle",
                Colores = delegate { } },
            new Variante { Nombre = "2. Backrooms", Idea = "Amarillo de oficina vacía, moqueta húmeda y fluorescentes",
                Colores = delegate { Base(C(26, 24, 12), C(18, 17, 8), C(40, 37, 18), C(70, 64, 32), C(240, 230, 190), C(190, 178, 128), C(130, 120, 80),
                    C(232, 212, 110), C(40, 34, 6), C(200, 190, 120), C(70, 62, 24)); Tema.Exito = C(190, 220, 120); Tema.Aviso = C(255, 200, 90); } },
            new Variante { Nombre = "3. Dreamcore", Idea = "Cielo pastel de sueño, nubes lila y celeste",
                Colores = delegate { Base(C(38, 32, 66), C(28, 24, 52), C(54, 46, 92), C(80, 70, 128), C(250, 246, 255), C(206, 196, 240), C(150, 140, 196),
                    C(150, 214, 255), C(14, 30, 60), C(255, 190, 240), C(70, 60, 120)); Tema.Exito = C(170, 255, 210); Tema.Aviso = C(255, 236, 150); } },
            new Variante { Nombre = "4. El vacío", Idea = "Negro total, rojo sangre y blanco que vigila",
                Colores = delegate { Base(C(4, 4, 6), C(0, 0, 0), C(14, 12, 14), C(40, 20, 24), C(236, 232, 232), C(160, 150, 152), C(100, 90, 92),
                    C(220, 30, 50), C(255, 240, 240), C(200, 20, 40), C(60, 10, 16)); Tema.Exito = C(200, 200, 200); Tema.Aviso = C(255, 90, 90); } },
            new Variante { Nombre = "5. Internet 2003", Idea = "Azul Windows XP, cian de nube y verde de 'conectado'",
                Colores = delegate { Base(C(8, 22, 54), C(4, 14, 38), C(16, 38, 84), C(34, 64, 126), C(236, 244, 255), C(160, 190, 236), C(100, 130, 186),
                    C(110, 230, 255), C(4, 30, 50), C(255, 255, 255), C(20, 60, 110)); Tema.Exito = C(120, 255, 120); Tema.Aviso = C(255, 220, 90); } },
            new Variante { Nombre = "6. CRT fósforo", Idea = "Monitor antiguo: negro y verde brillante",
                Colores = delegate { Base(C(4, 10, 6), C(2, 6, 3), C(10, 22, 13), C(24, 56, 32), C(190, 255, 200), C(110, 200, 130), C(70, 130, 84),
                    C(70, 255, 120), C(0, 30, 10), C(70, 255, 120), C(14, 50, 24)); Tema.Exito = C(140, 255, 160); Tema.Aviso = C(220, 255, 100); } }
        };

        [STAThread]
        static void Main(string[] args)
        {
            string salida = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "opc_variantes");
            Directory.CreateDirectory(salida);
            Application.EnableVisualStyles();
            Tema.Inicializar();
            Form host = new Form();
            host.StartPosition = FormStartPosition.Manual;
            host.Location = new Point(-20000, -20000);
            host.ShowInTaskbar = false;
            host.Shown += async delegate
            {
                Bitmap[] capturas = new Bitmap[Lista.Length];
                for (int i = 0; i < Lista.Length; i++)
                {
                    Tema.Aplicar(Tema.TemaWeirdcore);
                    Lista[i].Colores();
                    MainForm f = new MainForm("resumen", null, 0, false);
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-20000, -20000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    await Task.Delay(5000);
                    Panel raiz = (Panel)typeof(MainForm).GetField("raiz", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                    capturas[i] = new Bitmap(raiz.Width, raiz.Height);
                    raiz.DrawToBitmap(capturas[i], new Rectangle(0, 0, raiz.Width, raiz.Height));
                    capturas[i].Save(Path.Combine(salida, "variante_" + (i + 1) + ".png"), ImageFormat.Png);
                    f.Close();
                    f.Dispose();
                }
                Hoja(capturas, Path.Combine(salida, "comparativa_weirdcore.png"));
                host.Close();
            };
            Application.Run(host);
        }

        // Hoja 2 x 3 con cada captura a mitad de tamaño y su nombre encima
        static void Hoja(Bitmap[] capturas, string ruta)
        {
            int w = capturas[0].Width / 2, h = capturas[0].Height / 2, margen = 24, cab = 54, cols = 2;
            int filas = (capturas.Length + cols - 1) / cols;
            using (Bitmap hoja = new Bitmap(cols * w + (cols + 1) * margen, filas * (h + cab) + (filas + 1) * margen + 40))
            using (Graphics g = Graphics.FromImage(hoja))
            {
                g.Clear(Color.FromArgb(12, 12, 16));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font titulo = new Font("Segoe UI Semibold", 18f))
                using (Font nombre = new Font("Segoe UI Semibold", 13f))
                using (Font idea = new Font("Segoe UI", 10f))
                {
                    g.DrawString("Variaciones del tema weirdcore · Optimizador de PC", titulo, Brushes.White, margen, 14);
                    for (int i = 0; i < capturas.Length; i++)
                    {
                        int x = margen + (i % cols) * (w + margen), y = 40 + margen + (i / cols) * (h + cab + margen);
                        g.DrawString(Lista[i].Nombre, nombre, Brushes.White, x, y);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(170, 170, 185))) g.DrawString(Lista[i].Idea, idea, b, x, y + 26);
                        g.DrawImage(capturas[i], new Rectangle(x, y + cab, w, h));
                        using (Pen p = new Pen(Color.FromArgb(60, 60, 70))) g.DrawRectangle(p, x, y + cab, w, h);
                    }
                }
                hoja.Save(ruta, ImageFormat.Png);
            }
        }
    }
}
