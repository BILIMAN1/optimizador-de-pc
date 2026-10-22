using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Ventana principal: barra lateral con las secciones y el contenedor de páginas.</summary>
    class MainForm : Form
    {
        public static MainForm Instancia { get; private set; }

        /// <summary>id, texto del menú, icono</summary>
        public static readonly string[,] Secciones =
        {
            { "resumen", "Resumen", Tema.IcoResumen },
            { "modojuego", "Modo juego", Tema.IcoJuego },
            { "limpieza", "Liberar espacio", Tema.IcoLimpieza },
            { "archivos", "Archivos grandes", Tema.IcoArchivos },
            { "duplicados", "Duplicados", Tema.IcoDuplicados },
            { "programas", "Programas", Tema.IcoProgramas },
            { "apps", "Apps basura", Tema.IcoApps },
            { "inicio", "Inicio de Windows", Tema.IcoInicio },
            { "procesos", "Procesos", Tema.IcoProcesos },
            { "diagnostico", "Diagnóstico", Tema.IcoDiagnostico },
            { "herramientas", "Herramientas", Tema.IcoHerramientas },
            { "historial", "Historial", Tema.IcoHistorial }
        };

        // Animación de los adornos (murciélagos que aletean, ojo que parpadea, estrellas que brillan)
        private readonly Timer animacion = new Timer();
        private int pasoAnimacion;
        private Panel panelLogo, panelPie;

        /// <summary>Si no es null, la ventana se cerró para volver a abrirse con este tema.</summary>
        public string TemaSiguiente { get; private set; }

        // Código Konami: ↑ ↑ ↓ ↓ ← → ← → B A (solo se escucha tras pulsar "Desarrollada por Biliman")
        private static readonly Keys[] Konami =
        {
            Keys.Up, Keys.Up, Keys.Down, Keys.Down, Keys.Left, Keys.Right, Keys.Left, Keys.Right, Keys.B, Keys.A
        };
        private int pasoKonami;
        private DateTime escuchandoHasta = DateTime.MinValue;
        private Label lblAutor;
        /// <summary>Página que se estaba viendo (para reabrir en la misma tras cambiar de tema).</summary>
        public string PaginaActual { get { return actual; } }

        public const string Version = "1.0";

        private readonly Panel raiz, barra, contenedor;
        private readonly Dictionary<string, PaginaBase> paginas = new Dictionary<string, PaginaBase>();
        private readonly Dictionary<string, BotonNav> botones = new Dictionary<string, BotonNav>();
        private readonly string paginaInicial, carpetaCaptura;
        private readonly int esperaCaptura;
        private readonly bool capturarSoloInicial;
        private string actual;

        public MainForm(string paginaInicial, string carpetaCaptura, int esperaCaptura, bool capturarSoloInicial)
        {
            Instancia = this;
            this.paginaInicial = string.IsNullOrEmpty(paginaInicial) ? "resumen" : paginaInicial;
            this.carpetaCaptura = carpetaCaptura;
            this.esperaCaptura = esperaCaptura;
            this.capturarSoloInicial = capturarSoloInicial;

            Text = Tema.Halloween ? "Optimizador de PC · Edición Halloween · por Biliman"
                 : Tema.Weirdcore ? "0ptimizad0r de PC ▒ ¿sigues ahí? ▒ por Biliman"
                 : "Optimizador de PC · por Biliman";
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            Font = Tema.Fuente(9.5f);
            DoubleBuffered = true;
            Size = new Size(Tema.S(1220), Tema.S(800));
            MinimumSize = new Size(Tema.S(1000), Tema.S(700));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            if (carpetaCaptura != null)
            {
                // Modo de captura de pantallas (pruebas): la ventana se abre fuera de la pantalla
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-20000, -20000);
                ShowInTaskbar = false;
            }

            raiz = new Panel();
            raiz.Dock = DockStyle.Fill;
            raiz.BackColor = Tema.Fondo;

            barra = new Panel();
            barra.Dock = DockStyle.Left;
            barra.Width = Tema.S(236);
            barra.BackColor = Tema.Barra;
            barra.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Tema.Borde)) e.Graphics.DrawLine(p, barra.Width - 1, 0, barra.Width - 1, barra.Height);
            };

            contenedor = new Panel();
            contenedor.Dock = DockStyle.Fill;
            contenedor.BackColor = Tema.Fondo;

            ConstruirBarra();

            raiz.Controls.Add(contenedor);
            raiz.Controls.Add(barra);
            contenedor.BringToFront();
            Controls.Add(raiz);

            if (Tema.Halloween || Tema.Weirdcore)
            {
                animacion.Interval = 60;
                animacion.Tick += delegate
                {
                    pasoAnimacion++;
                    panelPie.Invalidate(new Rectangle(panelPie.Width - Tema.S(80), 0, Tema.S(80), Tema.S(52)));
                    if (Tema.Weirdcore && pasoAnimacion % 3 == 0) panelLogo.Invalidate(new Rectangle(panelLogo.Width - Tema.S(44), Tema.S(30), Tema.S(44), Tema.S(58)));
                };
                animacion.Start();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animacion.Dispose();
            base.Dispose(disposing);
        }

        // Ojo: abierto casi siempre y un parpadeo rápido cada ~5 segundos
        private float AperturaOjo()
        {
            int c = pasoAnimacion % 85;
            if (c == 0 || c == 4) return 0.55f;
            if (c >= 1 && c <= 3) return 0.05f;
            return 1f;
        }

        private void ConstruirBarra()
        {
            Panel logo = new Panel();
            logo.Dock = DockStyle.Top;
            logo.Height = Tema.S(92);
            logo.Paint += delegate(object s, PaintEventArgs e)
            {
                if (Tema.Halloween)
                {
                    Tema.DibujarTelarana(e.Graphics, new Rectangle(logo.Width - Tema.S(64), 0, Tema.S(64), Tema.S(64)), Color.FromArgb(70, Tema.TextoSuave));
                }
                else if (Tema.Weirdcore)
                {
                    Tema.DibujarNube(e.Graphics, logo.Width - Tema.S(70), Tema.S(6), Tema.S(54), Color.FromArgb(60, Tema.Morado));
                    int brillo = 90 + (int)(70 * Math.Abs(Math.Sin(pasoAnimacion * 0.06)));
                    Tema.DibujarEstrellas(e.Graphics, new Rectangle(logo.Width - Tema.S(40), Tema.S(34), Tema.S(34), Tema.S(50)), Color.FromArgb(brillo, Tema.Texto));
                }
                Tema.DibujarLogo(e.Graphics, new Rectangle(Tema.S(22), Tema.S(28), Tema.S(36), Tema.S(36)));
            };
            Label t1 = Tema.Etiqueta("Optimizador", 13f, Tema.Texto, true);
            t1.Location = new Point(Tema.S(68), Tema.S(26));
            Label t2 = Tema.Halloween ? Tema.Etiqueta("de PC · Halloween", 9f, Tema.Acento)
                     : Tema.Weirdcore ? Tema.Etiqueta("de PC · ¿estás despierto?", 9f, Tema.Acento)
                     : Tema.Etiqueta("de PC", 9f, Tema.TextoSuave);
            t2.Location = new Point(Tema.S(70), Tema.S(50));
            logo.Controls.Add(t1);
            logo.Controls.Add(t2);
            panelLogo = logo;

            // Si la ventana es baja y no caben todas las secciones, la lista se desplaza
            FlowLayoutPanel nav = new FlowLayoutPanel();
            nav.Dock = DockStyle.Fill;
            nav.FlowDirection = FlowDirection.TopDown;
            nav.WrapContents = false;
            nav.AutoScroll = true;
            nav.Padding = new Padding(Tema.S(12), 0, Tema.S(12), 0);
            nav.HandleCreated += delegate { Tema.ScrollOscuro(nav); };
            int ancho = barra.Width - Tema.S(25);
            for (int i = 0; i < Secciones.GetLength(0); i++)
            {
                BotonNav b = new BotonNav(Secciones[i, 0], Secciones[i, 1], Secciones[i, 2]);
                b.Width = ancho;
                b.Height = Tema.S(38);
                b.Click += delegate(object s, EventArgs e) { Navegar(((BotonNav)s).Id); };
                botones[b.Id] = b;
                nav.Controls.Add(b);
            }

            Panel pie = new Panel();
            pie.Dock = DockStyle.Bottom;
            pie.Height = Tema.S(132);
            pie.Padding = new Padding(Tema.S(22), 0, Tema.S(12), 0);
            pie.Paint += delegate(object s, PaintEventArgs e)
            {
                if (Tema.Weirdcore)
                {
                    // Franja de tablero de ajedrez, muy típica del weirdcore
                    Tema.DibujarAjedrez(e.Graphics, new Rectangle(0, pie.Height - Tema.S(8), pie.Width, Tema.S(8)), Tema.S(4));
                    Tema.DibujarOjo(e.Graphics, new RectangleF(pie.Width - Tema.S(58), Tema.S(18), Tema.S(40), Tema.S(20)),
                        Color.FromArgb(170, Tema.Morado), AperturaOjo());
                    return;
                }
                if (!Tema.Halloween) return;
                // Dos murciélagos que aletean a distinto ritmo y suben y bajan un poco
                float f = pasoAnimacion * 0.55f;
                Tema.DibujarMurcielago(e.Graphics, pie.Width - Tema.S(62), Tema.S(20) + (float)Math.Sin(f / 4) * Tema.S(3), Tema.S(30),
                    Color.FromArgb(150, Tema.Morado), 0.45f + 0.55f * (float)Math.Abs(Math.Sin(f)));
                Tema.DibujarMurcielago(e.Graphics, pie.Width - Tema.S(32), Tema.S(36) + (float)Math.Sin(f / 3 + 1) * Tema.S(3), Tema.S(20),
                    Color.FromArgb(110, Tema.Morado), 0.45f + 0.55f * (float)Math.Abs(Math.Sin(f * 1.3f + 1)));
            };
            panelPie = pie;
            Label estado = Tema.Etiqueta(Util.EsAdmin ? "●  Modo administrador" : "●  Sin permisos de administrador", 9f,
                Util.EsAdmin ? Tema.Exito : Tema.Aviso);
            estado.Location = new Point(Tema.S(22), Tema.S(54));
            Label autor1 = Tema.Etiqueta("Desarrollada por", 8.5f, Tema.TextoTenue);
            autor1.Location = new Point(Tema.S(22), Tema.S(80));
            Label autor2 = Tema.Etiqueta("Biliman", 8.5f, Tema.Acento, true);
            autor2.Location = new Point(autor1.Left + autor1.PreferredWidth + Tema.S(1), Tema.S(80));
            Label version = Tema.Etiqueta("Versión " + Version + (Tema.Halloween ? " · Edición Halloween" : Tema.Weirdcore ? " · no estás solo" : ""),
                8.5f, Tema.TextoTenue);
            version.Location = new Point(Tema.S(22), Tema.S(100));

            // Pulsar la firma activa la escucha del código secreto (no da ninguna pista a propósito)
            lblAutor = autor2;
            foreach (Label l in new[] { autor1, autor2 })
            {
                l.Click += delegate { EmpezarEscuchaSecreta(); };
            }

            // Botón para cambiar de tema (la ventana se vuelve a abrir al momento con los nuevos colores)
            string siguiente = Tema.Siguiente();
            Boton tema = new Boton(Tema.NombreVisible(siguiente), EstiloBoton.Fantasma, Tema.IcoTema);
            tema.Font = Tema.Fuente(9f);
            tema.Height = Tema.S(30);
            tema.Location = new Point(Tema.S(12), Tema.S(14));
            tema.Click += delegate { AlternarTema(siguiente); };
            pie.Controls.Add(tema);
            pie.Controls.Add(estado);
            pie.Controls.Add(autor1);
            pie.Controls.Add(autor2);
            pie.Controls.Add(version);

            barra.Controls.Add(nav);
            barra.Controls.Add(logo);
            barra.Controls.Add(pie);
            nav.BringToFront();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.OscurecerTitulo(this);
        }

        // Cierra la ventana para que Program la vuelva a abrir con el otro tema.
        // Si alguna página está trabajando (p. ej. limpiando) y no deja cerrar, no se cambia nada.
        private void AlternarTema(string destino)
        {
            TemaSiguiente = destino;
            Close();
            if (!IsDisposed && Visible) TemaSiguiente = null;
        }

        // ------------------------------------------------------------------ secreto

        private async void EmpezarEscuchaSecreta()
        {
            pasoKonami = 0;
            escuchandoHasta = DateTime.Now.AddSeconds(20);
            Focus();
            // Un parpadeo mínimo de la firma: la única señal de que "algo" escucha
            Color original = lblAutor.ForeColor;
            lblAutor.ForeColor = Tema.Texto;
            await System.Threading.Tasks.Task.Delay(140);
            if (!IsDisposed) lblAutor.ForeColor = original;
        }

        // Se mira antes que cualquier control (así las flechas no las "come" una tabla) y nunca se bloquea la tecla
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (DateTime.Now <= escuchandoHasta)
            {
                Keys tecla = keyData & Keys.KeyCode;
                if (tecla == Konami[pasoKonami]) pasoKonami++;
                else pasoKonami = tecla == Konami[0] ? 1 : 0;
                if (pasoKonami == Konami.Length)
                {
                    pasoKonami = 0;
                    escuchandoHasta = DateTime.MinValue;
                    BeginInvoke(new Action(DesbloquearSecreto));
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void DesbloquearSecreto()
        {
            bool yaEstaba = Tema.WeirdcoreDesbloqueado;
            Tema.DesbloquearWeirdcore();
            Util.Log("Tema secreto weirdcore " + (yaEstaba ? "activado de nuevo" : "desbloqueado"));
            Sonidos.Secreto();
            Dialogo.Info(this, "¿Hola?",
                yaEstaba ? "Ya habías estado aquí antes. Bienvenido de vuelta."
                         : "Has encontrado algo que no debías.\n\nTema weirdcore desbloqueado. A partir de ahora también podrás elegirlo con el botón de tema.");
            if (!Tema.Weirdcore) AlternarTema(Tema.TemaWeirdcore);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Navegar(paginaInicial);
            if (carpetaCaptura != null) Capturar();
        }

        private PaginaBase Crear(string id)
        {
            switch (id)
            {
                case "resumen": return new PaginaResumen();
                case "limpieza": return new PaginaLimpieza();
                case "inicio": return new PaginaInicio();
                case "procesos": return new PaginaProcesos();
                case "programas": return new PaginaProgramas();
                case "apps": return new PaginaApps();
                case "archivos": return new PaginaArchivos();
                case "herramientas": return new PaginaHerramientas();
                case "historial": return new PaginaHistorial();
                case "modojuego": return new PaginaModoJuego();
                case "duplicados": return new PaginaDuplicados();
                case "diagnostico": return new PaginaDiagnostico();
            }
            return null;
        }

        /// <summary>Muestra la página con ese id (la crea la primera vez).</summary>
        public void Navegar(string id)
        {
            if (!botones.ContainsKey(id)) id = "resumen";
            PaginaBase p;
            if (!paginas.TryGetValue(id, out p))
            {
                p = Crear(id);
                if (p == null) return;
                p.Visible = false;
                paginas[id] = p;
                contenedor.Controls.Add(p);
            }
            if (actual != id)
            {
                PaginaBase anterior;
                if (actual != null && paginas.TryGetValue(actual, out anterior)) anterior.Visible = false;
                foreach (BotonNav b in botones.Values) b.Seleccionado = b.Id == id;
                p.Visible = true;
                p.BringToFront();
                actual = id;
            }
            p.AlMostrar();
        }

        // Solo para pruebas: guarda una imagen PNG de cada página y cierra la app
        private async void Capturar()
        {
            try
            {
                Directory.CreateDirectory(carpetaCaptura);
                for (int i = 0; i < Secciones.GetLength(0); i++)
                {
                    string id = Secciones[i, 0];
                    if (capturarSoloInicial && id != paginaInicial) continue;
                    Navegar(id);
                    await Task.Delay(esperaCaptura);
                    using (Bitmap b = new Bitmap(raiz.Width, raiz.Height))
                    {
                        raiz.DrawToBitmap(b, new Rectangle(0, 0, raiz.Width, raiz.Height));
                        b.Save(Path.Combine(carpetaCaptura, string.Format("{0:00}_{1}.png", i + 1, id)), ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex)
            {
                Util.Log("Error en captura: " + ex);
            }
            Close();
        }
    }
}
