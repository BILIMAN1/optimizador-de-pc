using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OptimizadorPC
{
    enum EstiloBoton { Primario, Secundario, Peligro, Fantasma }

    /// <summary>Botón plano redondeado. Implementa IButtonControl para usarse como AcceptButton/CancelButton.</summary>
    class Boton : Control, IButtonControl
    {
        private bool hover, presionado, autoAncho = true;
        private EstiloBoton estilo;
        private string icono;
        private DialogResult resultado = DialogResult.None;

        public Boton(string texto) : this(texto, EstiloBoton.Secundario, null) { }
        public Boton(string texto, EstiloBoton estilo) : this(texto, estilo, null) { }

        public Boton(string texto, EstiloBoton estilo, string icono)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            this.estilo = estilo;
            this.icono = icono;
            Font = Tema.FuenteSemibold(9.5f);
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = new Padding(0, 0, Tema.S(8), 0);
            Height = Tema.S(36);
            Text = texto;
            AjustarAncho();
        }

        public EstiloBoton Estilo { get { return estilo; } set { estilo = value; Invalidate(); } }
        public string Icono { get { return icono; } set { icono = value; if (autoAncho) AjustarAncho(); Invalidate(); } }
        /// <summary>Si es true (por defecto) el ancho se ajusta al texto.</summary>
        public bool AutoAncho { get { return autoAncho; } set { autoAncho = value; if (value) AjustarAncho(); } }

        public DialogResult DialogResult { get { return resultado; } set { resultado = value; } }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        public void AjustarAncho()
        {
            int ancho = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(36);
            if (!string.IsNullOrEmpty(icono)) ancho += Tema.S(24);
            Width = ancho;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (resultado != DialogResult.None)
            {
                Form f = FindForm();
                if (f != null) f.DialogResult = resultado;
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (autoAncho) AjustarAncho(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (autoAncho) AjustarAncho(); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; presionado = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { presionado = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); presionado = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) PerformClick();
        }

        private void Colores(out Color fondo, out Color texto)
        {
            if (!Enabled)
            {
                fondo = estilo == EstiloBoton.Fantasma ? Color.Empty : Tema.ControlApagado;
                texto = Tema.TextoTenue;
                return;
            }
            switch (estilo)
            {
                case EstiloBoton.Primario:
                    fondo = presionado ? Tema.AcentoOscuro : hover ? Tema.AcentoHover : Tema.Acento;
                    texto = Tema.AcentoTexto;
                    break;
                case EstiloBoton.Peligro:
                    fondo = presionado ? Color.FromArgb(50, 24, 26) : hover ? Color.FromArgb(84, 38, 41) : Tema.PeligroFondo;
                    texto = Tema.Peligro;
                    break;
                case EstiloBoton.Fantasma:
                    fondo = presionado ? Tema.Control : hover ? Tema.TarjetaHover : Color.Empty;
                    texto = hover ? Tema.Texto : Tema.TextoSuave;
                    break;
                default:
                    fondo = presionado ? Tema.TarjetaHover : hover ? Tema.ControlHover : Tema.Control;
                    texto = Tema.Texto;
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fondo, texto;
            Colores(out fondo, out texto);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (fondo != Color.Empty)
            {
                using (GraphicsPath p = Tema.Redondeado(r, Tema.S(8)))
                using (SolidBrush b = new SolidBrush(fondo))
                {
                    g.FillPath(b, p);
                }
            }
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath p = Tema.Redondeado(r, Tema.S(8)))
                using (Pen pen = new Pen(Tema.AcentoHover, 1f))
                {
                    g.DrawPath(pen, p);
                }
            }

            int anchoIcono = string.IsNullOrEmpty(icono) ? 0 : Tema.S(24);
            Size st = TextRenderer.MeasureText(g, Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding);
            int x = Math.Max(Tema.S(8), (Width - anchoIcono - st.Width) / 2);
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (anchoIcono > 0)
            {
                TextRenderer.DrawText(g, icono, Tema.FuenteIconos(10f), new Rectangle(x, 0, anchoIcono, Height), texto, f);
            }
            TextRenderer.DrawText(g, Text ?? "", Font, new Rectangle(x + anchoIcono, 0, Width - x - anchoIcono, Height), texto,
                f | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Panel con esquinas redondeadas. Los hijos heredan su color de fondo.</summary>
    class Tarjeta : Panel
    {
        private int radio;
        private bool conBorde;

        public Tarjeta()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Tema.Tarjeta;
            ForeColor = Tema.Texto;
            Padding = new Padding(Tema.S(20));
            radio = Tema.S(12);
        }

        public int Radio { get { return radio; } set { radio = value; Invalidate(); } }
        public bool ConBorde { get { return conBorde; } set { conBorde = value; Invalidate(); } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ColorExterior());
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), radio))
            {
                using (SolidBrush b = new SolidBrush(BackColor)) g.FillPath(b, p);
                if (conBorde)
                {
                    using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
                }
            }
        }

        private Color ColorExterior()
        {
            Control p = Parent;
            while (p != null && p.BackColor.A < 255) p = p.Parent;
            return p == null ? Tema.Fondo : p.BackColor;
        }
    }

    /// <summary>Anillo de progreso (por ejemplo, uso del disco) con texto en el centro.</summary>
    class AnilloUso : Control
    {
        private double valor, mostrado;
        private string textoCentro = "", subtexto = "";
        private Color color = Tema.Acento;
        private int grosor;
        private readonly Timer animacion;

        public AnilloUso()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            grosor = Tema.S(12);
            Size = new Size(Tema.S(160), Tema.S(160));
            FuenteCentro = Tema.FuenteSemibold(22f);
            // El arco se llena poco a poco hasta el valor nuevo
            animacion = new Timer();
            animacion.Interval = 16;
            animacion.Tick += delegate(object s, EventArgs e)
            {
                mostrado += (valor - mostrado) * 0.14;
                if (Math.Abs(valor - mostrado) < 0.15) { mostrado = valor; animacion.Stop(); }
                Invalidate();
            };
        }

        /// <summary>De 0 a 100.</summary>
        public double Valor
        {
            get { return valor; }
            set
            {
                valor = Math.Max(0, Math.Min(100, value));
                if (Math.Abs(valor - mostrado) >= 0.15) animacion.Start();
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animacion.Dispose();
            base.Dispose(disposing);
        }
        public string TextoCentro { get { return textoCentro; } set { textoCentro = value ?? ""; Invalidate(); } }
        public string Subtexto { get { return subtexto; } set { subtexto = value ?? ""; Invalidate(); } }
        public Color ColorValor { get { return color; } set { color = value; Invalidate(); } }
        public int Grosor { get { return grosor; } set { grosor = value; Invalidate(); } }
        public Font FuenteCentro { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int lado = Math.Min(Width, Height) - grosor - 2;
            if (lado <= 0) return;
            Rectangle r = new Rectangle((Width - lado) / 2, (Height - lado) / 2, lado, lado);
            using (Pen pista = new Pen(Tema.Borde, grosor))
            {
                g.DrawEllipse(pista, r);
            }
            if (mostrado > 0.2)
            {
                using (Pen p = new Pen(color, grosor))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawArc(p, r, -90f, (float)(mostrado * 3.6));
                }
            }
            Size st = TextRenderer.MeasureText(g, textoCentro, FuenteCentro);
            Size ss = string.IsNullOrEmpty(subtexto) ? Size.Empty : TextRenderer.MeasureText(g, subtexto, Tema.Fuente(9f));
            int y = (Height - st.Height - ss.Height) / 2;
            TextRenderer.DrawText(g, textoCentro, FuenteCentro, new Rectangle(0, y, Width, st.Height), Tema.Texto,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            if (ss.Height > 0)
            {
                TextRenderer.DrawText(g, subtexto, Tema.Fuente(9f), new Rectangle(0, y + st.Height, Width, ss.Height), Tema.TextoSuave,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Barra fina redondeada. Valor de 0 a 100, o modo indeterminado animado.</summary>
    class BarraUso : Control
    {
        private double valor;
        private Color color = Tema.Acento;
        private bool indeterminado;
        private float fase;
        private readonly Timer animacion;

        public BarraUso()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Height = Tema.S(8);
            animacion = new Timer();
            animacion.Interval = 30;
            animacion.Tick += delegate(object s, EventArgs e) { fase += 0.018f; if (fase > 1.4f) fase = -0.4f; Invalidate(); };
        }

        public double Valor { get { return valor; } set { valor = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public Color ColorRelleno { get { return color; } set { color = value; Invalidate(); } }

        public bool Indeterminado
        {
            get { return indeterminado; }
            set
            {
                indeterminado = value;
                fase = -0.4f;
                animacion.Enabled = value;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animacion.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            float radio = Height / 2f;
            using (GraphicsPath p = Tema.Redondeado(r, radio))
            using (SolidBrush b = new SolidBrush(Tema.Borde))
            {
                g.FillPath(b, p);
            }
            RectangleF lleno;
            if (indeterminado)
            {
                float ancho = r.Width * 0.3f;
                float x = r.Width * fase;
                lleno = RectangleF.Intersect(new RectangleF(x, 0, ancho, r.Height), r);
            }
            else
            {
                lleno = new RectangleF(0, 0, (float)(r.Width * valor / 100.0), r.Height);
            }
            if (lleno.Width >= 1)
            {
                using (GraphicsPath p = Tema.Redondeado(lleno, Math.Min(radio, lleno.Width / 2)))
                using (SolidBrush b = new SolidBrush(color))
                {
                    g.FillPath(b, p);
                }
            }
        }
    }

    /// <summary>Interruptor de encendido/apagado.</summary>
    class Interruptor : Control
    {
        private bool activado, hover;

        public event EventHandler CambioEstado;

        public Interruptor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(Tema.S(44), Tema.S(24));
            Cursor = Cursors.Hand;
        }

        public bool Activado
        {
            get { return activado; }
            set { if (activado == value) return; activado = value; Invalidate(); if (CambioEstado != null) CambioEstado(this, EventArgs.Empty); }
        }

        /// <summary>Cambia el estado sin disparar CambioEstado.</summary>
        public void Establecer(bool valor) { activado = valor; Invalidate(); }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Activado = !activado; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Color pista = activado ? (hover ? Tema.AcentoHover : Tema.Acento) : (hover ? Tema.InterruptorApagadoHover : Tema.InterruptorApagado);
            if (!Enabled) pista = Tema.ControlApagado;
            using (GraphicsPath p = Tema.Redondeado(r, r.Height / 2))
            using (SolidBrush b = new SolidBrush(pista))
            {
                g.FillPath(b, p);
            }
            float d = r.Height - Tema.S(6);
            float x = activado ? r.Right - d - Tema.S(3) : Tema.S(3);
            using (SolidBrush b = new SolidBrush(activado ? Tema.AcentoTexto : Tema.Texto))
            {
                g.FillEllipse(b, x, Tema.S(3), d, d);
            }
        }
    }

    /// <summary>Botón de la barra lateral.</summary>
    class BotonNav : Control
    {
        private bool hover, seleccionado;
        private readonly string icono;

        public BotonNav(string id, string texto, string icono)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Id = id;
            Text = texto;
            this.icono = icono;
            Height = Tema.S(42);
            Font = Tema.Fuente(10f);
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 0, Tema.S(2));
        }

        public string Id { get; private set; }
        public bool Seleccionado { get { return seleccionado; } set { seleccionado = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (seleccionado || hover)
            {
                Color fondo = seleccionado ? Tema.NavSeleccion : Tema.NavHover;
                using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Tema.S(8)))
                using (SolidBrush b = new SolidBrush(fondo))
                {
                    g.FillPath(b, p);
                }
            }
            if (seleccionado)
            {
                using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, Tema.S(11), Tema.S(3), Height - Tema.S(22)), Tema.S(2)))
                using (SolidBrush b = new SolidBrush(Tema.Acento))
                {
                    g.FillPath(b, p);
                }
            }
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, icono, Tema.FuenteIconos(11.5f), new Rectangle(Tema.S(16), 0, Tema.S(24), Height),
                seleccionado ? Tema.Acento : Tema.TextoSuave, f);
            TextRenderer.DrawText(g, Text, seleccionado ? Tema.FuenteSemibold(10f) : Font,
                new Rectangle(Tema.S(48), 0, Width - Tema.S(52), Height), seleccionado ? Tema.Texto : Tema.TextoSuave, f | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Caja de texto redondeada con icono opcional y texto de ayuda.</summary>
    class CajaTexto : Panel
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private readonly string icono;
        private readonly string ayuda;
        public readonly TextBox Caja;

        public CajaTexto(string textoAyuda, string icono)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            this.icono = icono;
            ayuda = textoAyuda;
            BackColor = Tema.CajaFondo;
            Height = Tema.S(38);
            Width = Tema.S(260);
            Padding = new Padding(string.IsNullOrEmpty(icono) ? Tema.S(12) : Tema.S(36), Tema.S(9), Tema.S(12), Tema.S(4));
            Cursor = Cursors.IBeam;

            Caja = new TextBox();
            Caja.BorderStyle = BorderStyle.None;
            Caja.BackColor = BackColor;
            Caja.ForeColor = Tema.Texto;
            Caja.Font = Tema.Fuente(10f);
            Caja.Dock = DockStyle.Fill;
            Caja.HandleCreated += delegate(object s, EventArgs e) { SendMessage(Caja.Handle, EM_SETCUEBANNER, (IntPtr)1, ayuda); };
            Caja.GotFocus += delegate(object s, EventArgs e) { Invalidate(); };
            Caja.LostFocus += delegate(object s, EventArgs e) { Invalidate(); };
            Controls.Add(Caja);
        }

        public override string Text { get { return Caja.Text; } set { Caja.Text = value; } }

        public event EventHandler TextoCambiado
        {
            add { Caja.TextChanged += value; }
            remove { Caja.TextChanged -= value; }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Caja.Focus(); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Control p = Parent;
            while (p != null && p.BackColor.A < 255) p = p.Parent;
            g.Clear(p == null ? Tema.Fondo : p.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath gp = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Tema.S(8)))
            {
                using (SolidBrush b = new SolidBrush(BackColor)) g.FillPath(b, gp);
                using (Pen pen = new Pen(Caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, gp);
            }
            if (!string.IsNullOrEmpty(icono))
            {
                TextRenderer.DrawText(g, icono, Tema.FuenteIconos(10f), new Rectangle(Tema.S(12), 0, Tema.S(20), Height), Tema.TextoSuave,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }
    }
}
