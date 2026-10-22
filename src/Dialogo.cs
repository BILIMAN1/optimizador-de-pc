using System;
using System.Drawing;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Cuadros de diálogo con el tema oscuro de la app. Llamar SIEMPRE desde el hilo de la interfaz.</summary>
    class Dialogo : Form
    {
        private Dialogo(string titulo, string mensaje, string icono, Color colorIcono, string textoSi, string textoNo, EstiloBoton estiloSi)
        {
            tituloDialogo = titulo;
            mensajeDialogo = mensaje;
            Text = "Optimizador de PC";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            Font = Tema.Fuente(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Tema.S(24), Tema.S(22), Tema.S(24), Tema.S(20));
            MinimumSize = new Size(Tema.S(400), 0);

            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 2;
            t.RowCount = 3;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.Location = new Point(Padding.Left, Padding.Top);

            Label ico = Tema.Icono(icono, 20f, colorIcono);
            ico.Margin = new Padding(0, Tema.S(2), Tema.S(16), 0);
            t.Controls.Add(ico, 0, 0);
            t.SetRowSpan(ico, 2);

            Label lt = Tema.Etiqueta(titulo, 12.5f, Tema.Texto, true);
            lt.MaximumSize = new Size(Tema.S(440), 0);
            lt.Margin = new Padding(0, 0, 0, Tema.S(8));
            t.Controls.Add(lt, 1, 0);

            Label lm = Tema.Etiqueta(mensaje ?? "", 10f, Tema.TextoSuave);
            lm.MaximumSize = new Size(Tema.S(440), 0);
            lm.Margin = new Padding(0, 0, 0, Tema.S(22));
            t.Controls.Add(lm, 1, 1);

            FlowLayoutPanel botones = new FlowLayoutPanel();
            botones.FlowDirection = FlowDirection.RightToLeft;
            botones.AutoSize = true;
            botones.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            botones.Dock = DockStyle.Fill;
            botones.Margin = new Padding(0);
            botones.WrapContents = false;

            Boton si = new Boton(textoSi, estiloSi);
            si.DialogResult = DialogResult.OK;
            si.Margin = new Padding(Tema.S(8), 0, 0, 0);
            botones.Controls.Add(si);
            AcceptButton = si;
            if (textoNo != null)
            {
                Boton no = new Boton(textoNo, EstiloBoton.Secundario);
                no.DialogResult = DialogResult.Cancel;
                no.Margin = new Padding(Tema.S(8), 0, 0, 0);
                botones.Controls.Add(no);
                CancelButton = no;
            }
            else
            {
                CancelButton = si;
            }
            t.Controls.Add(botones, 0, 2);
            t.SetColumnSpan(botones, 2);
            Controls.Add(t);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.OscurecerTitulo(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (AcceptButton is Control) ((Control)AcceptButton).Focus();
        }

        /// <summary>
        /// Solo para las pruebas automáticas: si no es null, los diálogos no se muestran y
        /// esta función decide la respuesta a partir del título y el mensaje.
        /// </summary>
        internal static Func<string, string, bool> RespuestaAutomatica;

        private string tituloDialogo, mensajeDialogo;

        private static DialogResult Mostrar(IWin32Window dueno, Dialogo d)
        {
            using (d)
            {
                if (RespuestaAutomatica != null)
                {
                    bool si = RespuestaAutomatica(d.tituloDialogo, d.mensajeDialogo);
                    return si ? DialogResult.OK : DialogResult.Cancel;
                }
                if (dueno == null && MainForm.Instancia != null && !MainForm.Instancia.IsDisposed) dueno = MainForm.Instancia;
                return dueno != null ? d.ShowDialog(dueno) : d.ShowDialog();
            }
        }

        /// <summary>Pregunta Sí/No. Con peligro=true el botón de aceptar sale en rojo.</summary>
        public static bool Confirmar(IWin32Window dueno, string titulo, string mensaje, string textoSi, bool peligro)
        {
            Dialogo d = new Dialogo(titulo, mensaje, peligro ? Tema.IcoAviso : Tema.IcoInfo, peligro ? Tema.Aviso : Tema.Acento,
                textoSi, "Cancelar", peligro ? EstiloBoton.Peligro : EstiloBoton.Primario);
            return Mostrar(dueno, d) == DialogResult.OK;
        }

        public static void Info(IWin32Window dueno, string titulo, string mensaje)
        {
            Mostrar(dueno, new Dialogo(titulo, mensaje, Tema.IcoInfo, Tema.Acento, "Entendido", null, EstiloBoton.Primario));
        }

        public static void Exito(IWin32Window dueno, string titulo, string mensaje)
        {
            Sonidos.Exito();
            Mostrar(dueno,new Dialogo(titulo, mensaje, Tema.IcoCheck, Tema.Exito, "Genial", null, EstiloBoton.Primario));
        }

        public static void Error(IWin32Window dueno, string titulo, string mensaje)
        {
            Sonidos.Error();
            Mostrar(dueno,new Dialogo(titulo, mensaje, Tema.IcoAviso, Tema.Peligro, "Cerrar", null, EstiloBoton.Secundario));
        }
    }
}
