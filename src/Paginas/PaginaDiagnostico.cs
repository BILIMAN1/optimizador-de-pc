using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Diagnóstico de solo lectura: velocidad de arranque, qué lo frena, actualizaciones y controladores.</summary>
    class PaginaDiagnostico : PaginaBase
    {
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        private readonly Label lblSegundos, lblValoracion, lblNotaArranque;
        private readonly GraficoArranques grafico;
        private readonly Label lblUltima, lblReinicio, lblPendientes;
        private readonly Boton btnBuscarAct;
        private readonly DataGridView tablaCausas, tablaDrivers;
        private readonly Label lblSinCausas, lblCargandoDrivers;
        private bool cargando, cargado, buscandoAct;

        public PaginaDiagnostico() : base("Diagnóstico", "Velocidad de arranque, actualizaciones y controladores (solo se mira, no se cambia nada)")
        {
            Boton actualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            actualizar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            actualizar.Click += delegate { cargado = false; Cargar(); };
            Acciones.Controls.Add(actualizar);

            // ---- Arranque ----
            lblSegundos = Tema.Etiqueta("—", 26f, Tema.Texto, true);
            lblValoracion = Tema.Etiqueta("Leyendo…", 10f, Tema.TextoSuave);
            lblValoracion.Margin = new Padding(Tema.S(10), Tema.S(18), 0, 0);
            FlowLayoutPanel cifra = new FlowLayoutPanel();
            cifra.AutoSize = true;
            cifra.WrapContents = false;
            cifra.Controls.Add(lblSegundos);
            cifra.Controls.Add(lblValoracion);
            grafico = new GraficoArranques();
            grafico.Dock = DockStyle.Fill;
            lblNotaArranque = Tema.Etiqueta("Tiempo hasta que el escritorio está listo, en los últimos arranques completos.", 8.5f, Tema.TextoTenue);
            Tarjeta tArranque = TarjetaCon("Velocidad de arranque", Tema.IcoReloj, cifra, grafico, lblNotaArranque);

            // ---- Actualizaciones ----
            lblUltima = Tema.Etiqueta("Leyendo…", 10f, Tema.Texto);
            lblReinicio = Tema.Etiqueta("", 9.5f, Tema.Aviso);
            lblReinicio.Margin = new Padding(0, Tema.S(6), 0, 0);
            lblPendientes = Tema.Etiqueta("Pulsa «Buscar» para preguntar a Windows Update qué falta (no se instala nada).", 9.5f, Tema.TextoSuave);
            lblPendientes.MaximumSize = new Size(Tema.S(420), 0);
            lblPendientes.Margin = new Padding(0, Tema.S(10), 0, 0);
            FlowLayoutPanel lineas = new FlowLayoutPanel();
            lineas.Dock = DockStyle.Fill;
            lineas.FlowDirection = FlowDirection.TopDown;
            lineas.WrapContents = false;
            lineas.Controls.Add(lblUltima);
            lineas.Controls.Add(lblReinicio);
            lineas.Controls.Add(lblPendientes);
            btnBuscarAct = new Boton("Buscar actualizaciones", EstiloBoton.Secundario, Tema.IcoBuscar);
            btnBuscarAct.Click += delegate { BuscarActualizaciones(); };
            Boton abrirWU = new Boton("Abrir Windows Update", EstiloBoton.Fantasma);
            abrirWU.Click += delegate { Util.Abrir("ms-settings:windowsupdate", null); };
            FlowLayoutPanel botonesAct = new FlowLayoutPanel();
            botonesAct.AutoSize = true;
            botonesAct.WrapContents = false;
            botonesAct.Controls.Add(btnBuscarAct);
            botonesAct.Controls.Add(abrirWU);
            Tarjeta tAct = TarjetaCon("Actualizaciones de Windows", Tema.IcoActualizar, null, lineas, botonesAct);

            // ---- Lo que frena el arranque ----
            tablaCausas = NuevaTabla();
            tablaCausas.Columns.Add(Columna("Nombre", 50, false));
            tablaCausas.Columns.Add(Columna("Tipo", 18, false));
            tablaCausas.Columns.Add(Columna("Veces", 12, true));
            tablaCausas.Columns.Add(Columna("Retraso", 16, true));
            tablaCausas.Columns[1].DefaultCellStyle.ForeColor = Tema.TextoSuave;
            lblSinCausas = Tema.Etiqueta("Leyendo…", 10f, Tema.TextoSuave);
            lblSinCausas.AutoSize = false;
            lblSinCausas.Dock = DockStyle.Fill;
            lblSinCausas.TextAlign = ContentAlignment.MiddleCenter;
            Panel zonaCausas = new Panel();
            zonaCausas.Dock = DockStyle.Fill;
            zonaCausas.Controls.Add(tablaCausas);
            zonaCausas.Controls.Add(lblSinCausas);
            Boton verInicio = new Boton("Ver programas de inicio", EstiloBoton.Fantasma, Tema.IcoInicio);
            verInicio.Click += delegate { IrA("inicio"); };
            Tarjeta tCausas = TarjetaCon("Lo que frena el arranque", Tema.IcoAviso, null, zonaCausas, verInicio);

            // ---- Controladores ----
            tablaDrivers = NuevaTabla();
            tablaDrivers.Columns.Add(Columna("Dispositivo", 46, false));
            tablaDrivers.Columns.Add(Columna("Tipo", 16, false));
            tablaDrivers.Columns.Add(Columna("Versión", 20, false));
            tablaDrivers.Columns.Add(Columna("Fecha", 18, false));
            tablaDrivers.Columns[1].DefaultCellStyle.ForeColor = Tema.TextoSuave;
            tablaDrivers.Columns[2].DefaultCellStyle.ForeColor = Tema.TextoTenue;
            tablaDrivers.Columns[2].DefaultCellStyle.Font = Tema.Fuente(8.5f);
            tablaDrivers.Columns[1].DefaultCellStyle.Padding = new Padding(Tema.S(6), 0, Tema.S(4), 0);
            tablaDrivers.Columns[2].DefaultCellStyle.Padding = new Padding(Tema.S(6), 0, Tema.S(4), 0);
            tablaDrivers.Columns[3].DefaultCellStyle.Padding = new Padding(Tema.S(6), 0, Tema.S(6), 0);
            // Las columnas cortas con ancho fijo: el nombre del dispositivo se lleva el resto
            FijarAncho(tablaDrivers.Columns[1], 70);
            FijarAncho(tablaDrivers.Columns[2], 92);
            FijarAncho(tablaDrivers.Columns[3], 70);
            FijarAncho(tablaCausas.Columns[1], 104);
            FijarAncho(tablaCausas.Columns[2], 70);
            FijarAncho(tablaCausas.Columns[3], 84);
            lblCargandoDrivers = Tema.Etiqueta("Leyendo controladores…", 10f, Tema.TextoSuave);
            lblCargandoDrivers.AutoSize = false;
            lblCargandoDrivers.Dock = DockStyle.Fill;
            lblCargandoDrivers.TextAlign = ContentAlignment.MiddleCenter;
            Panel zonaDrivers = new Panel();
            zonaDrivers.Dock = DockStyle.Fill;
            zonaDrivers.Controls.Add(tablaDrivers);
            zonaDrivers.Controls.Add(lblCargandoDrivers);
            Label consejo = Tema.Etiqueta("La gráfica se actualiza con NVIDIA App o AMD Software; lo demás, con Windows Update. Aquí no se instala nada.",
                8.5f, Tema.TextoTenue);
            consejo.MaximumSize = new Size(Tema.S(440), 0);
            Tarjeta tDrivers = TarjetaCon("Controladores (drivers)", Tema.IcoHerramientas, null, zonaDrivers, consejo);

            TableLayoutPanel cuerpo = new TableLayoutPanel();
            cuerpo.Dock = DockStyle.Fill;
            cuerpo.ColumnCount = 2;
            cuerpo.RowCount = 2;
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(262)));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tArranque.Margin = new Padding(0, 0, Tema.S(8), Tema.S(16));
            tAct.Margin = new Padding(Tema.S(8), 0, 0, Tema.S(16));
            tCausas.Margin = new Padding(0, 0, Tema.S(8), 0);
            tDrivers.Margin = new Padding(Tema.S(8), 0, 0, 0);
            cuerpo.Controls.Add(tArranque, 0, 0);
            cuerpo.Controls.Add(tAct, 1, 0);
            cuerpo.Controls.Add(tCausas, 0, 1);
            cuerpo.Controls.Add(tDrivers, 1, 1);
            Contenido.Controls.Add(cuerpo);
        }

        // Tarjeta con título (icono + texto), una línea opcional bajo el título, el contenido y un pie
        private static Tarjeta TarjetaCon(string titulo, string icono, Control cabecera, Control contenido, Control pie)
        {
            Tarjeta t = new Tarjeta();
            t.Dock = DockStyle.Fill;
            t.Padding = Tema.S(20, 16, 20, 14);
            TableLayoutPanel tl = new TableLayoutPanel();
            tl.Dock = DockStyle.Fill;
            tl.ColumnCount = 1;
            tl.RowCount = 4;
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FlowLayoutPanel cab = new FlowLayoutPanel();
            cab.AutoSize = true;
            cab.WrapContents = false;
            cab.Margin = new Padding(0, 0, 0, Tema.S(6));
            Label ico = Tema.Icono(icono, 12f, Tema.Acento);
            ico.Margin = new Padding(0, Tema.S(2), Tema.S(10), 0);
            cab.Controls.Add(ico);
            cab.Controls.Add(Tema.Etiqueta(titulo, 11.5f, Tema.Texto, true));
            tl.Controls.Add(cab, 0, 0);
            if (cabecera != null) tl.Controls.Add(cabecera, 0, 1);
            contenido.Margin = new Padding(0, Tema.S(4), 0, Tema.S(6));
            tl.Controls.Add(contenido, 0, 2);
            if (pie != null) tl.Controls.Add(pie, 0, 3);
            t.Controls.Add(tl);
            return t;
        }

        private static DataGridView NuevaTabla()
        {
            DataGridView t = new DataGridView();
            Tema.EstilizarTabla(t);
            t.Dock = DockStyle.Fill;
            t.ReadOnly = true;
            t.RowTemplate.Height = Tema.S(34);
            t.ColumnHeadersHeight = Tema.S(34);
            return t;
        }

        private static void FijarAncho(DataGridViewColumn c, int px)
        {
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            c.Width = Tema.S(px);
        }

        private static DataGridViewTextBoxColumn Columna(string titulo, float peso, bool derecha)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (derecha)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            return c;
        }

        public override void AlMostrar()
        {
            // Solo lectura; la búsqueda de actualizaciones (lenta) solo se hace al pulsar el botón
            if (!cargado) Cargar();
        }

        private async void Cargar()
        {
            if (cargando) return;
            cargando = true;
            try
            {
                Task<InfoArranque> tArr = Task.Run(() => Diagnostico.LeerArranque());
                Task<InfoActualizaciones> tAct = Task.Run(() => Diagnostico.LeerEstadoActualizaciones());
                Task<List<Controlador>> tDrv = Task.Run(() => Diagnostico.LeerControladores());

                InfoActualizaciones act = await tAct;
                if (IsDisposed) return;
                MostrarActualizaciones(act);
                InfoArranque arr = await tArr;
                if (IsDisposed) return;
                MostrarArranque(arr);
                List<Controlador> drv = await tDrv;
                if (IsDisposed) return;
                MostrarControladores(drv);
                cargado = true;
            }
            catch (Exception ex)
            {
                Util.Log("Diagnóstico: error al leer: " + ex);
            }
            finally
            {
                cargando = false;
            }
        }

        private void MostrarArranque(InfoArranque a)
        {
            grafico.Arranques = a.Arranques;
            if (a.Arranques.Count == 0)
            {
                lblSegundos.Text = "—";
                lblValoracion.Text = a.SinPermiso || !Util.EsAdmin ? "Necesita permisos de administrador" : "Aún no hay datos";
                lblValoracion.ForeColor = Tema.Aviso;
                lblNotaArranque.Text = "Windows apunta el tiempo de cada arranque completo (no cuenta el «Inicio rápido»).";
            }
            else
            {
                Arranque ultimo = a.Arranques[a.Arranques.Count - 1];
                lblSegundos.Text = ultimo.Segundos.ToString("N0", Es) + " s";
                Color c = ultimo.Segundos < 30 ? Tema.Exito : ultimo.Segundos < 60 ? Tema.Aviso : Tema.Peligro;
                lblSegundos.ForeColor = c;
                lblValoracion.ForeColor = c;
                lblValoracion.Text = (ultimo.Segundos < 30 ? "Arranque rápido" : ultimo.Segundos < 60 ? "Arranque normal" : "Arranque lento") +
                    " · " + ultimo.Fecha.ToString("d MMM", Es);
            }

            tablaCausas.Rows.Clear();
            foreach (CausaLenta c in a.Causas)
            {
                tablaCausas.Rows.Add(c.Nombre, c.Tipo, c.Veces, c.RetrasoMax > 0 ? "+" + c.RetrasoMax.ToString("N1", Es) + " s" : "—");
            }
            tablaCausas.ClearSelection();
            tablaCausas.Visible = a.Causas.Count > 0;
            lblSinCausas.Visible = a.Causas.Count == 0;
            lblSinCausas.Text = a.SinPermiso || !Util.EsAdmin ? "Para ver esto abre la app como administrador."
                : "Windows no ha detectado nada que frene el arranque en los últimos 90 días. ¡Bien!";
        }

        private void MostrarActualizaciones(InfoActualizaciones i)
        {
            if (i.UltimaInstalada.HasValue)
            {
                int dias = (int)(DateTime.Today - i.UltimaInstalada.Value.Date).TotalDays;
                lblUltima.Text = "Última actualización instalada: " + i.UltimaInstalada.Value.ToString("d 'de' MMMM", Es) +
                    (dias == 0 ? " (hoy)" : " (hace " + dias + (dias == 1 ? " día)" : " días)"));
                lblUltima.ForeColor = dias > 45 ? Tema.Aviso : Tema.Texto;
            }
            else
            {
                lblUltima.Text = "No se pudo saber cuándo fue la última actualización.";
            }
            lblReinicio.Text = i.ReinicioPendiente ? "Hay actualizaciones esperando a que reinicies el PC." : "";
            lblReinicio.Visible = i.ReinicioPendiente;
        }

        private void MostrarControladores(List<Controlador> l)
        {
            tablaDrivers.Rows.Clear();
            foreach (Controlador c in l)
            {
                string fecha = c.Fecha.HasValue ? c.Fecha.Value.ToString("MM/yyyy", Es) : "—";
                int i = tablaDrivers.Rows.Add(c.Dispositivo, c.Tipo, c.Version, fecha);
                if (c.Fecha.HasValue)
                {
                    double anos = (DateTime.Today - c.Fecha.Value).TotalDays / 365.0;
                    // La gráfica se actualiza a menudo: más de 6 meses ya es viejo. Lo demás, más de 3 años.
                    bool viejo = c.Tipo == "Gráfica" ? anos > 0.5 : anos > 3;
                    if (viejo)
                    {
                        tablaDrivers.Rows[i].Cells[3].Style.ForeColor = Tema.Aviso;
                        tablaDrivers.Rows[i].Cells[3].ToolTipText = "Tiene más de " + (c.Tipo == "Gráfica" ? "6 meses" : "3 años") + ": puede haber uno más nuevo";
                    }
                }
            }
            tablaDrivers.ClearSelection();
            tablaDrivers.Visible = l.Count > 0;
            lblCargandoDrivers.Visible = l.Count == 0;
            lblCargandoDrivers.Text = "No se pudieron leer los controladores.";
        }

        private async void BuscarActualizaciones()
        {
            if (buscandoAct) return;
            buscandoAct = true;
            btnBuscarAct.Enabled = false;
            lblPendientes.ForeColor = Tema.TextoSuave;
            lblPendientes.Text = "Preguntando a Windows Update… puede tardar un par de minutos.";
            try
            {
                string error = null;
                List<string> l = await Task.Run(() => Diagnostico.BuscarActualizaciones(out error));
                if (IsDisposed) return;
                if (error != null)
                {
                    lblPendientes.ForeColor = Tema.Aviso;
                    lblPendientes.Text = "No se pudo consultar Windows Update (" + Util.Recortar(error, 80) + ").";
                }
                else if (l.Count == 0)
                {
                    lblPendientes.ForeColor = Tema.Exito;
                    lblPendientes.Text = "Windows está al día: no falta ninguna actualización.";
                }
                else
                {
                    lblPendientes.ForeColor = Tema.Aviso;
                    string lista = "";
                    for (int i = 0; i < l.Count && i < 3; i++) lista += "\n•  " + Util.Recortar(l[i], 70);
                    lblPendientes.Text = "Faltan " + l.Count + (l.Count == 1 ? " actualización:" : " actualizaciones:") + lista +
                        (l.Count > 3 ? "\n…y " + (l.Count - 3) + " más. Instálalas desde Windows Update." : "\nInstálalas desde Windows Update.");
                }
            }
            finally
            {
                buscandoAct = false;
                if (!IsDisposed) btnBuscarAct.Enabled = true;
            }
        }

        /// <summary>Barras con el tiempo de los últimos arranques (la última, resaltada).</summary>
        private class GraficoArranques : Control
        {
            private List<Arranque> arranques = new List<Arranque>();

            public GraficoArranques()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            public List<Arranque> Arranques { set { arranques = value ?? new List<Arranque>(); Invalidate(); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (arranques.Count == 0) return;
                int n = arranques.Count;
                int abajo = Tema.S(18), arriba = Tema.S(16);
                float alto = Height - abajo - arriba;
                double maximo = 30;
                foreach (Arranque a in arranques) maximo = Math.Max(maximo, a.Segundos);
                float hueco = Tema.S(6);
                float ancho = Math.Min(Tema.S(34), (Width - hueco * (n - 1)) / (float)n);
                float x = (Width - (ancho * n + hueco * (n - 1))) / 2;
                for (int i = 0; i < n; i++)
                {
                    Arranque a = arranques[i];
                    float h = (float)(alto * a.Segundos / maximo);
                    RectangleF r = new RectangleF(x, arriba + alto - h, ancho, Math.Max(2, h));
                    bool ultimo = i == n - 1;
                    Color c = a.Segundos < 30 ? Tema.Exito : a.Segundos < 60 ? Tema.Aviso : Tema.Peligro;
                    using (GraphicsPath p = Tema.Redondeado(r, Tema.S(4)))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(ultimo ? 255 : 110, c)))
                    {
                        g.FillPath(b, p);
                    }
                    if (ultimo || n <= 8)
                    {
                        TextRenderer.DrawText(g, a.Segundos.ToString("N0") + "s", Tema.Fuente(7.5f),
                            new Rectangle((int)(x - Tema.S(8)), (int)r.Y - Tema.S(15), (int)ancho + Tema.S(16), Tema.S(14)),
                            ultimo ? Tema.Texto : Tema.TextoTenue, TextFormatFlags.HorizontalCenter);
                    }
                    TextRenderer.DrawText(g, a.Fecha.ToString("dd/MM"), Tema.Fuente(7f),
                        new Rectangle((int)(x - Tema.S(8)), Height - abajo + Tema.S(3), (int)ancho + Tema.S(16), abajo),
                        Tema.TextoTenue, TextFormatFlags.HorizontalCenter);
                    x += ancho + hueco;
                }
            }
        }
    }
}
