using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Colores, fuentes, iconos y escalado DPI compartidos por toda la app.</summary>
    static class Tema
    {
        // ---- Colores: los rellena Aplicar() según el tema elegido (Halloween o normal) ----
        public static Color Fondo, Barra, Tarjeta, TarjetaHover, Control, ControlHover, Borde;
        public static Color Texto, TextoSuave, TextoTenue;
        public static Color Acento, AcentoHover, AcentoOscuro, AcentoTexto;
        public static Color Exito, Aviso, Peligro, PeligroFondo, Seleccion, Morado;
        // Tonos de controles concretos
        public static Color ControlApagado, NavSeleccion, NavHover, CajaFondo, InterruptorApagado, InterruptorApagadoHover, FilaAlterna;

        public const string TemaHalloween = "halloween", TemaNormal = "normal", TemaWeirdcore = "weirdcore";

        /// <summary>Tema actual: "halloween", "normal" o "weirdcore" (oculto, se desbloquea con el código Konami).</summary>
        public static string Nombre { get; private set; }
        public static bool Halloween { get { return Nombre == TemaHalloween; } }
        public static bool Weirdcore { get { return Nombre == TemaWeirdcore; } }

        static Tema() { Aplicar(TemaHalloween); }

        /// <summary>Solo para las pruebas automáticas: otra carpeta, para no tocar el tema ni el secreto reales.</summary>
        internal static string CarpetaPrueba;
        private static string Carpeta { get { return CarpetaPrueba ?? Util.CarpetaDatos; } }
        private static string ArchivoPreferencia { get { return System.IO.Path.Combine(Carpeta, "tema.txt"); } }
        private static string ArchivoSecreto { get { return System.IO.Path.Combine(Carpeta, "secreto.dat"); } }

        /// <summary>Tema guardado por el usuario (Halloween si nunca eligió).</summary>
        public static string LeerPreferencia()
        {
            try
            {
                string t = System.IO.File.ReadAllText(ArchivoPreferencia).Trim();
                if (t == TemaNormal) return TemaNormal;
                if (t == TemaWeirdcore && WeirdcoreDesbloqueado) return TemaWeirdcore;
            }
            catch { }
            return TemaHalloween;
        }

        public static void GuardarPreferencia(string tema)
        {
            try { System.IO.File.WriteAllText(ArchivoPreferencia, tema); } catch { }
        }

        public static bool WeirdcoreDesbloqueado
        {
            get { try { return System.IO.File.Exists(ArchivoSecreto); } catch { return false; } }
        }

        public static void DesbloquearWeirdcore()
        {
            try { System.IO.File.WriteAllText(ArchivoSecreto, "¿sigues ahí?"); } catch { }
        }

        /// <summary>El tema que viene después al pulsar "cambiar tema".</summary>
        public static string Siguiente()
        {
            if (Halloween) return TemaNormal;
            if (Nombre == TemaNormal && WeirdcoreDesbloqueado) return TemaWeirdcore;
            return TemaHalloween;
        }

        public static string NombreVisible(string tema)
        {
            if (tema == TemaNormal) return "Tema normal";
            if (tema == TemaWeirdcore) return "Tema weirdcore";
            return "Tema Halloween";
        }

        /// <summary>Carga los colores de un tema. Solo afecta a las ventanas que se creen después.</summary>
        public static void Aplicar(string tema)
        {
            Nombre = tema == TemaNormal || tema == TemaWeirdcore ? tema : TemaHalloween;
            if (Weirdcore)
            {
                // Weirdcore: azul noche de pantalla vieja, rosa chicle y cian de nube, todo un poco "fuera de lugar"
                Fondo = Color.FromArgb(9, 10, 26); Barra = Color.FromArgb(5, 5, 16);
                Tarjeta = Color.FromArgb(19, 21, 46); TarjetaHover = Color.FromArgb(28, 31, 62);
                Control = Color.FromArgb(34, 37, 74); ControlHover = Color.FromArgb(46, 50, 94); Borde = Color.FromArgb(44, 48, 92);
                Texto = Color.FromArgb(232, 234, 255); TextoSuave = Color.FromArgb(158, 164, 214); TextoTenue = Color.FromArgb(104, 110, 160);
                Acento = Color.FromArgb(255, 128, 206); AcentoHover = Color.FromArgb(255, 170, 224);
                AcentoOscuro = Color.FromArgb(214, 84, 168); AcentoTexto = Color.FromArgb(46, 6, 34);
                Exito = Color.FromArgb(134, 255, 186); Aviso = Color.FromArgb(255, 241, 118);
                Peligro = Color.FromArgb(255, 99, 99); PeligroFondo = Color.FromArgb(70, 20, 40);
                Seleccion = Color.FromArgb(64, 30, 72); Morado = Color.FromArgb(118, 234, 255);
                ControlApagado = Color.FromArgb(26, 28, 56); NavSeleccion = Color.FromArgb(30, 26, 60); NavHover = Color.FromArgb(18, 18, 40);
                CajaFondo = Color.FromArgb(26, 28, 56); InterruptorApagado = Color.FromArgb(54, 58, 104);
                InterruptorApagadoHover = Color.FromArgb(70, 74, 124); FilaAlterna = Color.FromArgb(22, 24, 52);
            }
            else if (Halloween)
            {
                // Noche morada, calabaza y verde moco
                Fondo = Color.FromArgb(17, 12, 22); Barra = Color.FromArgb(11, 7, 15);
                Tarjeta = Color.FromArgb(28, 20, 36); TarjetaHover = Color.FromArgb(38, 28, 48);
                Control = Color.FromArgb(45, 33, 57); ControlHover = Color.FromArgb(58, 43, 72); Borde = Color.FromArgb(52, 39, 64);
                Texto = Color.FromArgb(242, 236, 247); TextoSuave = Color.FromArgb(172, 157, 188); TextoTenue = Color.FromArgb(114, 99, 130);
                Acento = Color.FromArgb(255, 140, 26); AcentoHover = Color.FromArgb(255, 172, 82);
                AcentoOscuro = Color.FromArgb(214, 102, 8); AcentoTexto = Color.FromArgb(42, 16, 0);
                Exito = Color.FromArgb(163, 230, 53); Aviso = Color.FromArgb(250, 204, 21);
                Peligro = Color.FromArgb(251, 92, 92); PeligroFondo = Color.FromArgb(68, 22, 30);
                Seleccion = Color.FromArgb(66, 36, 22); Morado = Color.FromArgb(168, 85, 247);
                ControlApagado = Color.FromArgb(36, 27, 45); NavSeleccion = Color.FromArgb(36, 24, 40); NavHover = Color.FromArgb(24, 17, 30);
                CajaFondo = Color.FromArgb(36, 27, 45); InterruptorApagado = Color.FromArgb(64, 48, 80);
                InterruptorApagadoHover = Color.FromArgb(80, 62, 98); FilaAlterna = Color.FromArgb(31, 22, 40);
            }
            else
            {
                // Gris azulado oscuro con acento verde azulado
                Fondo = Color.FromArgb(15, 17, 21); Barra = Color.FromArgb(11, 13, 16);
                Tarjeta = Color.FromArgb(24, 27, 33); TarjetaHover = Color.FromArgb(32, 36, 44);
                Control = Color.FromArgb(38, 43, 52); ControlHover = Color.FromArgb(48, 54, 65); Borde = Color.FromArgb(42, 47, 56);
                Texto = Color.FromArgb(232, 235, 240); TextoSuave = Color.FromArgb(154, 163, 178); TextoTenue = Color.FromArgb(100, 108, 122);
                Acento = Color.FromArgb(45, 212, 191); AcentoHover = Color.FromArgb(94, 234, 212);
                AcentoOscuro = Color.FromArgb(20, 160, 142); AcentoTexto = Color.FromArgb(4, 40, 36);
                Exito = Color.FromArgb(74, 222, 128); Aviso = Color.FromArgb(251, 191, 36);
                Peligro = Color.FromArgb(248, 113, 113); PeligroFondo = Color.FromArgb(62, 30, 33);
                Seleccion = Color.FromArgb(28, 56, 54); Morado = Color.FromArgb(129, 140, 248);
                ControlApagado = Color.FromArgb(32, 35, 42); NavSeleccion = Color.FromArgb(28, 33, 41); NavHover = Color.FromArgb(21, 24, 30);
                CajaFondo = Color.FromArgb(32, 36, 44); InterruptorApagado = Color.FromArgb(56, 62, 74);
                InterruptorApagadoHover = Color.FromArgb(70, 76, 90); FilaAlterna = Color.FromArgb(27, 30, 37);
            }
        }

        // ---- Iconos (fuente Segoe MDL2 Assets, presente en Windows 10 y 11) ----
        public const string IcoResumen = "";
        public const string IcoLimpieza = "";
        public const string IcoInicio = "";
        public const string IcoProcesos = "";
        public const string IcoProgramas = "";
        public const string IcoApps = "";
        public const string IcoArchivos = "";
        public const string IcoHerramientas = "";
        public const string IcoBuscar = "";
        public const string IcoActualizar = "";
        public const string IcoCheck = "";
        public const string IcoAviso = "";
        public const string IcoInfo = "";
        public const string IcoDisco = "";
        public const string IcoCancelar = "";
        public const string IcoPlay = "";
        public const string IcoReloj = "";
        public const string IcoEscudo = "";
        public const string IcoHistorial = "";
        public const string IcoTema = "";
        public const string IcoJuego = "";
        public const string IcoDuplicados = "";
        public const string IcoDiagnostico = "";
        public const string IcoAccesoDirecto = "";
        public const string IcoSonido = "";

        private static float escala = 1f;
        private static readonly Dictionary<string, Font> fuentes = new Dictionary<string, Font>();

        public static void Inicializar()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                escala = g.DpiX / 96f;
            }
        }

        public static float Escala { get { return escala; } }

        /// <summary>Escala un tamaño en píxeles de diseño (a 96 DPI) al DPI real de la pantalla.</summary>
        public static int S(int px) { return (int)Math.Round(px * escala); }

        public static Padding S(int izquierda, int arriba, int derecha, int abajo)
        {
            return new Padding(S(izquierda), S(arriba), S(derecha), S(abajo));
        }

        public static Font Fuente(float puntos) { return ObtenerFuente("Segoe UI", puntos, FontStyle.Regular); }
        public static Font Fuente(float puntos, FontStyle estilo) { return ObtenerFuente("Segoe UI", puntos, estilo); }
        public static Font FuenteSemibold(float puntos) { return ObtenerFuente("Segoe UI Semibold", puntos, FontStyle.Regular); }
        public static Font FuenteIconos(float puntos) { return ObtenerFuente("Segoe MDL2 Assets", puntos, FontStyle.Regular); }

        private static Font ObtenerFuente(string familia, float puntos, FontStyle estilo)
        {
            string clave = familia + "|" + puntos + "|" + (int)estilo;
            Font f;
            lock (fuentes)
            {
                if (!fuentes.TryGetValue(clave, out f))
                {
                    f = new Font(familia, puntos, estilo, GraphicsUnit.Point);
                    fuentes[clave] = f;
                }
            }
            return f;
        }

        /// <summary>Verde/teal si va bien, ámbar a partir de 75 %, rojo a partir de 90 %.</summary>
        public static Color ColorSegunUso(double porcentaje)
        {
            if (porcentaje >= 90) return Peligro;
            if (porcentaje >= 75) return Aviso;
            return Acento;
        }

        public static GraphicsPath Redondeado(RectangleF r, float radio)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            if (d <= 1)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Etiqueta autoajustable. Hereda el color de fondo del contenedor.</summary>
        public static Label Etiqueta(string texto, float puntos, Color color)
        {
            return Etiqueta(texto, puntos, color, false);
        }

        public static Label Etiqueta(string texto, float puntos, Color color, bool seminegrita)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = texto;
            l.Font = seminegrita ? FuenteSemibold(puntos) : Fuente(puntos);
            l.ForeColor = color;
            l.UseMnemonic = false;
            l.Margin = new Padding(0);
            return l;
        }

        /// <summary>Etiqueta con un icono MDL2.</summary>
        public static Label Icono(string glifo, float puntos, Color color)
        {
            Label l = Etiqueta(glifo, puntos, color);
            l.Font = FuenteIconos(puntos);
            return l;
        }

        /// <summary>Logo de la app: cuadrado redondeado con un rayo.</summary>
        public static void DibujarLogo(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Weirdcore)
            {
                // Weirdcore: un ojo que mira, sobre un cielo rosa y celeste
                using (GraphicsPath p = Redondeado(r, r.Width * 0.24f))
                using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(255, 176, 226), Color.FromArgb(118, 196, 255), 75f))
                {
                    g.FillPath(b, p);
                }
                DibujarOjo(g, Rel(r, 0.12f, 0.28f, 0.76f, 0.44f), Color.FromArgb(24, 18, 60));
                return;
            }
            if (!Halloween)
            {
                // Tema normal: rayo sobre cuadrado verde azulado
                using (GraphicsPath p = Redondeado(r, r.Width * 0.24f))
                using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(94, 234, 212), Color.FromArgb(13, 148, 136), 45f))
                {
                    g.FillPath(b, p);
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(6, 46, 42)))
                {
                    g.FillPolygon(b, Pts(r, 0.58f, 0.10f, 0.24f, 0.56f, 0.47f, 0.56f, 0.41f, 0.90f, 0.76f, 0.43f, 0.53f, 0.43f));
                }
                return;
            }

            // Fondo: cuadrado redondeado morado oscuro
            using (GraphicsPath p = Redondeado(r, r.Width * 0.24f))
            using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(91, 33, 182), Color.FromArgb(35, 10, 58), 60f))
            {
                g.FillPath(b, p);
            }

            // Calabaza: tres gajos naranjas
            RectangleF cuerpo = Rel(r, 0.12f, 0.30f, 0.76f, 0.58f);
            using (LinearGradientBrush b = new LinearGradientBrush(cuerpo, Color.FromArgb(255, 176, 64), Color.FromArgb(222, 88, 0), 90f))
            {
                g.FillEllipse(b, Rel(r, 0.12f, 0.31f, 0.44f, 0.56f));
                g.FillEllipse(b, Rel(r, 0.44f, 0.31f, 0.44f, 0.56f));
                g.FillEllipse(b, Rel(r, 0.27f, 0.29f, 0.46f, 0.60f));
            }
            if (r.Width >= 24)
            {
                using (Pen surco = new Pen(Color.FromArgb(150, 170, 64, 0), Math.Max(1f, r.Width / 40f)))
                {
                    g.DrawArc(surco, Rel(r, 0.27f, 0.29f, 0.46f, 0.60f), 100, 160);
                    g.DrawArc(surco, Rel(r, 0.27f, 0.29f, 0.46f, 0.60f), -80, 160);
                }
            }

            // Tallo verde
            using (SolidBrush b = new SolidBrush(Color.FromArgb(101, 163, 13)))
            {
                g.FillPolygon(b, Pts(r, 0.46f, 0.33f, 0.47f, 0.20f, 0.56f, 0.14f, 0.58f, 0.19f, 0.54f, 0.22f, 0.55f, 0.33f));
            }

            // Cara de jack-o'-lantern iluminada por dentro
            using (SolidBrush luz = new SolidBrush(Color.FromArgb(254, 240, 138)))
            {
                g.FillPolygon(luz, Pts(r, 0.29f, 0.56f, 0.43f, 0.56f, 0.36f, 0.44f));
                g.FillPolygon(luz, Pts(r, 0.57f, 0.56f, 0.71f, 0.56f, 0.64f, 0.44f));
                g.FillPolygon(luz, Pts(r, 0.27f, 0.65f, 0.36f, 0.70f, 0.42f, 0.65f, 0.50f, 0.71f, 0.58f, 0.65f, 0.64f, 0.70f,
                                        0.73f, 0.65f, 0.67f, 0.79f, 0.50f, 0.83f, 0.33f, 0.79f));
            }
        }

        private static RectangleF Rel(RectangleF r, float x, float y, float w, float h)
        {
            return new RectangleF(r.X + x * r.Width, r.Y + y * r.Height, w * r.Width, h * r.Height);
        }

        private static PointF[] Pts(RectangleF r, params float[] xy)
        {
            PointF[] p = new PointF[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new PointF(r.X + xy[i * 2] * r.Width, r.Y + xy[i * 2 + 1] * r.Height);
            return p;
        }

        /// <summary>Telaraña en la esquina superior derecha de 'r' (decoración de Halloween).</summary>
        public static void DibujarTelarana(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PointF centro = new PointF(r.Right, r.Top);
            float radio = Math.Min(r.Width, r.Height);
            using (Pen hilo = new Pen(color, Math.Max(1f, S(1))))
            {
                int rayos = 6;
                for (int i = 0; i <= rayos; i++)
                {
                    double a = Math.PI / 2 + Math.PI / 2 * i / rayos;
                    g.DrawLine(hilo, centro, new PointF(centro.X + (float)(Math.Cos(a) * radio), centro.Y + (float)(Math.Sin(a) * radio)));
                }
                for (int anillo = 1; anillo <= 4; anillo++)
                {
                    float d = radio * anillo / 4.4f;
                    PointF[] seg = new PointF[rayos + 1];
                    for (int i = 0; i <= rayos; i++)
                    {
                        double a = Math.PI / 2 + Math.PI / 2 * i / rayos;
                        seg[i] = new PointF(centro.X + (float)(Math.Cos(a) * d), centro.Y + (float)(Math.Sin(a) * d));
                    }
                    g.DrawCurve(hilo, seg, 0.15f);
                }
            }
        }

        /// <summary>Ojo que parpadea: apertura 1 = abierto, 0 = cerrado (una línea).</summary>
        public static void DibujarOjo(Graphics g, RectangleF r, Color tinta, float apertura)
        {
            if (apertura >= 0.99f) { DibujarOjo(g, r, tinta); return; }
            float cy = r.Top + r.Height / 2;
            if (apertura < 0.15f)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(tinta, Math.Max(1f, r.Height / 9f)))
                {
                    g.DrawBezier(p, new PointF(r.Left, cy), new PointF(r.Left + r.Width * 0.3f, cy + r.Height * 0.18f),
                                 new PointF(r.Right - r.Width * 0.3f, cy + r.Height * 0.18f), new PointF(r.Right, cy));
                }
                return;
            }
            DibujarOjo(g, new RectangleF(r.Left, cy - r.Height * apertura / 2, r.Width, r.Height * apertura), tinta);
        }

        /// <summary>Ojo abierto (contorno de almendra, iris y pupila) que ocupa 'r'. 'tinta' es el color del contorno y la pupila.</summary>
        public static void DibujarOjo(Graphics g, RectangleF r, Color tinta)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath almendra = new GraphicsPath())
            {
                PointF izq = new PointF(r.Left, r.Top + r.Height / 2), der = new PointF(r.Right, r.Top + r.Height / 2);
                almendra.AddBezier(izq, new PointF(r.Left + r.Width * 0.3f, r.Top - r.Height * 0.25f),
                                   new PointF(r.Right - r.Width * 0.3f, r.Top - r.Height * 0.25f), der);
                almendra.AddBezier(der, new PointF(r.Right - r.Width * 0.3f, r.Bottom + r.Height * 0.25f),
                                   new PointF(r.Left + r.Width * 0.3f, r.Bottom + r.Height * 0.25f), izq);
                using (SolidBrush blanco = new SolidBrush(Color.FromArgb(tinta.A, 250, 250, 255))) g.FillPath(blanco, almendra);
                float d = r.Height * 0.9f;
                RectangleF iris = new RectangleF(r.Left + r.Width / 2 - d / 2, r.Top + r.Height / 2 - d / 2, d, d);
                Region antes = g.Clip;
                g.SetClip(almendra, CombineMode.Intersect);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(tinta.A, 94, 214, 255))) g.FillEllipse(b, iris);
                float dp = d * 0.48f;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(tinta.A, 12, 10, 30)))
                    g.FillEllipse(b, iris.X + (d - dp) / 2, iris.Y + (d - dp) / 2, dp, dp);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(tinta.A, 255, 255, 255)))
                    g.FillEllipse(b, iris.X + d * 0.56f, iris.Y + d * 0.22f, d * 0.16f, d * 0.16f);
                g.Clip = antes;
                using (Pen p = new Pen(tinta, Math.Max(1f, r.Height / 9f))) g.DrawPath(p, almendra);
            }
        }

        /// <summary>Nube redondeada (decoración weirdcore).</summary>
        public static void DibujarNube(Graphics g, float x, float y, float ancho, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = ancho * 0.5f;
            using (SolidBrush b = new SolidBrush(color))
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(x, y + h * 0.35f, ancho * 0.45f, h * 0.6f);
                p.AddEllipse(x + ancho * 0.22f, y, ancho * 0.5f, h * 0.85f);
                p.AddEllipse(x + ancho * 0.5f, y + h * 0.25f, ancho * 0.5f, h * 0.7f);
                p.FillMode = FillMode.Winding;
                g.FillPath(b, p);
            }
        }

        /// <summary>Destellos de cuatro puntas repartidos por 'r' (siempre en el mismo sitio).</summary>
        public static void DibujarEstrellas(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Random azar = new Random(7);
            using (SolidBrush b = new SolidBrush(color))
            {
                for (int i = 0; i < 5; i++)
                {
                    float cx = r.X + (float)azar.NextDouble() * r.Width, cy = r.Y + (float)azar.NextDouble() * r.Height;
                    float t = S(3) + (float)azar.NextDouble() * S(3), f = t * 0.25f;
                    g.FillPolygon(b, new[]
                    {
                        new PointF(cx, cy - t), new PointF(cx + f, cy - f), new PointF(cx + t, cy), new PointF(cx + f, cy + f),
                        new PointF(cx, cy + t), new PointF(cx - f, cy + f), new PointF(cx - t, cy), new PointF(cx - f, cy - f)
                    });
                }
            }
        }

        /// <summary>Franja de tablero de ajedrez blanco y negro.</summary>
        public static void DibujarAjedrez(Graphics g, Rectangle r, int lado)
        {
            if (lado <= 0) return;
            using (SolidBrush claro = new SolidBrush(Color.FromArgb(90, 235, 235, 255)))
            using (SolidBrush oscuro = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            {
                for (int y = r.Top, fila = 0; y < r.Bottom; y += lado, fila++)
                {
                    for (int x = r.Left, col = 0; x < r.Right; x += lado, col++)
                    {
                        g.FillRectangle((fila + col) % 2 == 0 ? claro : oscuro, x, y, lado, lado);
                    }
                }
            }
        }

        /// <summary>Murciélago sencillo centrado en (x, y) con la envergadura indicada.</summary>
        public static void DibujarMurcielago(Graphics g, float x, float y, float ancho, Color color)
        {
            DibujarMurcielago(g, x, y, ancho, color, 1f);
        }

        /// <summary>Murciélago con las alas más o menos levantadas (aleteo de -1 a 1) para animarlo.</summary>
        public static void DibujarMurcielago(Graphics g, float x, float y, float ancho, Color color, float aleteo)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float a = ancho / 2, h = ancho * 0.32f * aleteo;
            PointF[] alas =
            {
                new PointF(x, y - h * 0.35f), new PointF(x - a * 0.35f, y - h * 0.75f), new PointF(x - a * 0.7f, y - h * 0.55f),
                new PointF(x - a, y - h * 0.9f), new PointF(x - a * 0.85f, y + h * 0.1f), new PointF(x - a * 0.62f, y - h * 0.05f),
                new PointF(x - a * 0.45f, y + h * 0.35f), new PointF(x - a * 0.22f, y + h * 0.05f), new PointF(x, y + h * 0.55f),
                new PointF(x + a * 0.22f, y + h * 0.05f), new PointF(x + a * 0.45f, y + h * 0.35f), new PointF(x + a * 0.62f, y - h * 0.05f),
                new PointF(x + a * 0.85f, y + h * 0.1f), new PointF(x + a, y - h * 0.9f), new PointF(x + a * 0.7f, y - h * 0.55f),
                new PointF(x + a * 0.35f, y - h * 0.75f)
            };
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, alas);
        }

        /// <summary>Aplica el estilo oscuro a un DataGridView y hace que las casillas se confirmen al instante.</summary>
        public static void EstilizarTabla(DataGridView t)
        {
            t.BackgroundColor = Tarjeta;
            t.BorderStyle = BorderStyle.None;
            t.GridColor = Borde;
            t.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            t.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            t.EnableHeadersVisualStyles = false;
            t.RowHeadersVisible = false;
            t.AllowUserToAddRows = false;
            t.AllowUserToDeleteRows = false;
            t.AllowUserToResizeRows = false;
            t.AllowUserToOrderColumns = false;
            t.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            t.MultiSelect = false;
            t.AutoGenerateColumns = false;
            t.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            t.ColumnHeadersHeight = S(40);
            t.RowTemplate.Height = S(38);
            t.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            DataGridViewCellStyle enc = t.ColumnHeadersDefaultCellStyle;
            enc.BackColor = Tarjeta;
            enc.ForeColor = TextoSuave;
            enc.SelectionBackColor = Tarjeta;
            enc.SelectionForeColor = TextoSuave;
            enc.Font = FuenteSemibold(9f);
            enc.Padding = new Padding(S(10), 0, S(10), 0);
            enc.WrapMode = DataGridViewTriState.False;

            DataGridViewCellStyle c = t.DefaultCellStyle;
            c.BackColor = Tarjeta;
            c.ForeColor = Texto;
            c.SelectionBackColor = Seleccion;
            c.SelectionForeColor = Texto;
            c.Font = Fuente(9.5f);
            c.Padding = new Padding(S(10), 0, S(10), 0);
            c.WrapMode = DataGridViewTriState.False;

            t.AlternatingRowsDefaultCellStyle.BackColor = FilaAlterna;

            PropertyInfo db = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            if (db != null) db.SetValue(t, true, null);

            t.CurrentCellDirtyStateChanged += delegate(object s, EventArgs e)
            {
                if (t.IsCurrentCellDirty && t.CurrentCell is DataGridViewCheckBoxCell)
                {
                    t.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            t.CellPainting += PintarCasilla;
            t.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            t.HandleCreated += delegate(object s, EventArgs e) { ScrollOscuro(t); };
        }

        // Casillas de verificación redondeadas en color acento en lugar de las del sistema
        private static void PintarCasilla(object sender, DataGridViewCellPaintingEventArgs e)
        {
            DataGridView t = (DataGridView)sender;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!(t.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)) return;

            e.PaintBackground(e.CellBounds, true);
            bool marcado = false;
            if (e.Value is bool) marcado = (bool)e.Value;
            else if (e.Value is CheckState) marcado = (CheckState)e.Value == CheckState.Checked;

            bool deshabilitado = t.Columns[e.ColumnIndex].ReadOnly || t.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly;
            int lado = S(18);
            Rectangle r = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - lado) / 2,
                                        e.CellBounds.Y + (e.CellBounds.Height - lado) / 2, lado, lado);
            Graphics g = e.Graphics;
            SmoothingMode anterior = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Redondeado(r, S(4)))
            {
                if (marcado)
                {
                    using (SolidBrush b = new SolidBrush(deshabilitado ? AcentoOscuro : Acento)) g.FillPath(b, p);
                    TextRenderer.DrawText(g, IcoCheck, FuenteIconos(8f), r, AcentoTexto,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else
                {
                    using (Pen pen = new Pen(deshabilitado ? Borde : TextoTenue, Math.Max(1f, S(1) * 1.5f))) g.DrawPath(pen, p);
                }
            }
            g.SmoothingMode = anterior;
            e.Handled = true;
        }

        // ---- Integración con Windows: barra de título y barras de desplazamiento oscuras ----

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamano);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void OscurecerTitulo(Form f)
        {
            try
            {
                int si = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref si, 4) != 0)
                {
                    DwmSetWindowAttribute(f.Handle, 19, ref si, 4);
                }
            }
            catch { }
        }

        /// <summary>Barras de desplazamiento oscuras para un control y sus barras hijas.</summary>
        public static void ScrollOscuro(Control c)
        {
            try
            {
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                foreach (Control h in c.Controls)
                {
                    if (h is ScrollBar && h.IsHandleCreated) SetWindowTheme(h.Handle, "DarkMode_Explorer", null);
                }
            }
            catch { }
        }
    }
}
