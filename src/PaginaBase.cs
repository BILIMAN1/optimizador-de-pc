using System.Drawing;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>
    /// Base de todas las pantallas: título, subtítulo, zona de botones a la derecha del título
    /// y el panel Contenido donde cada página pone lo suyo.
    /// </summary>
    abstract class PaginaBase : UserControl
    {
        /// <summary>Zona bajo el título. Ocupa todo el espacio restante.</summary>
        protected readonly Panel Contenido;
        /// <summary>Botones a la derecha del título (se colocan de derecha a izquierda).</summary>
        protected readonly FlowLayoutPanel Acciones;
        private readonly Label lblSubtitulo;

        protected PaginaBase(string titulo, string subtitulo)
        {
            Dock = DockStyle.Fill;
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            Font = Tema.Fuente(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Padding = new Padding(Tema.S(32), Tema.S(26), Tema.S(32), Tema.S(24));

            Panel encabezado = new Panel();
            encabezado.Dock = DockStyle.Top;
            encabezado.Height = Tema.S(84);

            Label lblTitulo = Tema.Etiqueta(titulo, 20f, Tema.Texto, true);
            lblTitulo.Location = new Point(0, 0);
            lblSubtitulo = Tema.Etiqueta(subtitulo, 10f, Tema.TextoSuave);
            lblSubtitulo.Location = new Point(Tema.S(2), Tema.S(44));

            Acciones = new FlowLayoutPanel();
            Acciones.Dock = DockStyle.Right;
            Acciones.AutoSize = true;
            Acciones.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Acciones.FlowDirection = FlowDirection.RightToLeft;
            Acciones.WrapContents = false;
            Acciones.Padding = new Padding(0, Tema.S(10), 0, 0);

            encabezado.Controls.Add(lblTitulo);
            encabezado.Controls.Add(lblSubtitulo);
            encabezado.Controls.Add(Acciones);

            Contenido = new Panel();
            Contenido.Dock = DockStyle.Fill;

            Controls.Add(Contenido);
            Controls.Add(encabezado);
            Contenido.BringToFront();
        }

        public string Subtitulo
        {
            get { return lblSubtitulo.Text; }
            set { lblSubtitulo.Text = value; }
        }

        /// <summary>
        /// Se llama cada vez que la página se hace visible. Debe ser de SOLO LECTURA
        /// (nunca borrar ni cambiar nada aquí), no bloquear la interfaz (usar Task.Run)
        /// y aguantar que se llame varias veces seguidas.
        /// </summary>
        public virtual void AlMostrar() { }

        /// <summary>Navega a otra página por su id ("resumen", "limpieza", "inicio"...).</summary>
        protected void IrA(string id)
        {
            if (MainForm.Instancia != null) MainForm.Instancia.Navegar(id);
        }
    }
}
