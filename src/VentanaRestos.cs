using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>
    /// Dos usos: (1) enseñar los restos que dejó un programa y dejar elegir cuáles borrar;
    /// (2) después de borrar, enseñar un informe con lo que se borró y lo que no, uno por uno.
    /// </summary>
    class VentanaRestos : Form
    {
        /// <summary>Solo para las pruebas automáticas: si no es null, decide sin mostrar la ventana.</summary>
        internal static Func<List<Resto>, List<Resto>> EleccionAutomatica;

        private readonly List<Resto> restos;
        private readonly bool informe;
        private readonly DataGridView tabla;
        private readonly Label lblTotal;
        private readonly Boton btnBorrar;

        private VentanaRestos(string titulo, string explicacion, List<Resto> restos, bool informe, string carpetaRespaldo)
        {
            this.restos = restos;
            this.informe = informe;
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
            ClientSize = new Size(Tema.S(860), Tema.S(540));
            Padding = new Padding(Tema.S(24), Tema.S(20), Tema.S(24), Tema.S(20));

            Label lblTitulo = Tema.Etiqueta(titulo, 14f, Tema.Texto, true);
            lblTitulo.Dock = DockStyle.Top;
            lblTitulo.AutoSize = false;
            lblTitulo.AutoEllipsis = true;
            lblTitulo.Height = Tema.S(34);

            Label detalle = Tema.Etiqueta(explicacion, 9.5f, Tema.TextoSuave);
            detalle.Dock = DockStyle.Top;
            detalle.AutoSize = false;
            detalle.Height = Tema.S(46);

            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = informe;
            tabla.ShowCellToolTips = true;
            if (!informe)
            {
                DataGridViewCheckBoxColumn colMarca = new DataGridViewCheckBoxColumn();
                colMarca.Width = Tema.S(48);
                colMarca.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                colMarca.SortMode = DataGridViewColumnSortMode.NotSortable;
                tabla.Columns.Add(colMarca);
            }
            tabla.Columns.Add(Columna("Tipo", 16, false));
            tabla.Columns.Add(Columna("Ubicación", informe ? 50 : 66, false));
            tabla.Columns.Add(Columna(informe ? "Resultado" : "Tamaño", informe ? 34 : 14, !informe));
            int colRuta = informe ? 1 : 2;
            tabla.Columns[colRuta].DefaultCellStyle.ForeColor = Tema.TextoSuave;

            foreach (Resto r in restos)
            {
                int i;
                if (informe)
                {
                    i = tabla.Rows.Add(NombreTipo(r.Tipo), r.Ruta, (r.Borrado ? "✓  " : "✗  ") + (r.Resultado ?? ""));
                    tabla.Rows[i].Cells[2].Style.ForeColor = r.Borrado ? Tema.Exito : Tema.Peligro;
                }
                else
                {
                    i = tabla.Rows.Add(r.Marcado, NombreTipo(r.Tipo), r.Ruta, r.Tamano >= 0 ? Util.FormatoTamano(r.Tamano) : "—");
                }
                tabla.Rows[i].Tag = r;
                tabla.Rows[i].Cells[colRuta].ToolTipText = r.Ruta + "\n" + r.Motivo;
            }
            tabla.CellValueChanged += delegate { ActualizarTotal(); };

            Tarjeta tarjeta = new Tarjeta();
            tarjeta.Dock = DockStyle.Fill;
            tarjeta.Padding = Tema.S(10, 6, 10, 10);
            tarjeta.Controls.Add(tabla);

            Panel abajo = new Panel();
            abajo.Dock = DockStyle.Bottom;
            abajo.Height = Tema.S(56);
            lblTotal = Tema.Etiqueta("", 10f, Tema.Texto, true);
            lblTotal.Location = new Point(0, Tema.S(24));
            abajo.Controls.Add(lblTotal);

            // Los botones van en una fila pegada a la derecha (de derecha a izquierda: el principal el último)
            FlowLayoutPanel botones = new FlowLayoutPanel();
            botones.Dock = DockStyle.Right;
            botones.AutoSize = true;
            botones.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            botones.FlowDirection = FlowDirection.RightToLeft;
            botones.WrapContents = false;
            botones.Padding = new Padding(0, Tema.S(14), 0, 0);
            if (informe)
            {
                Boton cerrar = new Boton("Cerrar", EstiloBoton.Primario);
                cerrar.DialogResult = DialogResult.OK;
                cerrar.Margin = new Padding(Tema.S(8), 0, 0, 0);
                botones.Controls.Add(cerrar);
                AcceptButton = cerrar;
                CancelButton = cerrar;
                if (!string.IsNullOrEmpty(carpetaRespaldo) && Directory.Exists(carpetaRespaldo))
                {
                    Boton copia = new Boton("Ver copia del registro", EstiloBoton.Fantasma, Tema.IcoArchivos);
                    copia.Margin = new Padding(Tema.S(8), 0, 0, 0);
                    copia.Click += delegate { Util.Abrir(carpetaRespaldo, null); };
                    botones.Controls.Add(copia);
                }
            }
            else
            {
                btnBorrar = new Boton("Borrar lo marcado", EstiloBoton.Peligro, Tema.IcoLimpieza);
                btnBorrar.DialogResult = DialogResult.OK;
                btnBorrar.Margin = new Padding(Tema.S(8), 0, 0, 0);
                Boton btnNo = new Boton("No borrar nada", EstiloBoton.Secundario);
                btnNo.DialogResult = DialogResult.Cancel;
                btnNo.Margin = new Padding(Tema.S(8), 0, 0, 0);
                botones.Controls.Add(btnBorrar);
                botones.Controls.Add(btnNo);
                CancelButton = btnNo;
            }
            abajo.Controls.Add(botones);

            Panel separador = new Panel();
            separador.Dock = DockStyle.Top;
            separador.Height = Tema.S(6);

            Controls.Add(tarjeta);
            Controls.Add(separador);
            Controls.Add(detalle);
            Controls.Add(lblTitulo);
            Controls.Add(abajo);
            tarjeta.BringToFront();
            ActualizarTotal();
        }

        private static DataGridViewTextBoxColumn Columna(string titulo, float peso, bool derecha)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (derecha)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            return c;
        }

        private static string NombreTipo(TipoResto t)
        {
            switch (t)
            {
                case TipoResto.Carpeta: return "Carpeta";
                case TipoResto.Archivo: return "Archivo";
                case TipoResto.AccesoDirecto: return "Acceso directo";
                default: return "Registro";
            }
        }

        private List<Resto> Marcados()
        {
            List<Resto> l = new List<Resto>();
            if (informe) return l;
            foreach (DataGridViewRow r in tabla.Rows)
            {
                object v = r.Cells[0].Value;
                if (v is bool && (bool)v) l.Add((Resto)r.Tag);
            }
            return l;
        }

        private void ActualizarTotal()
        {
            if (informe)
            {
                int ok = restos.FindAll(r => r.Borrado).Count;
                long bytes = 0;
                foreach (Resto r in restos) if (r.Borrado && r.Tamano > 0) bytes += r.Tamano;
                lblTotal.Text = ok + " de " + restos.Count + " borrados" + (bytes > 0 ? " · " + Util.FormatoTamano(bytes) : "");
                lblTotal.ForeColor = ok == restos.Count ? Tema.Exito : Tema.Aviso;
                return;
            }
            List<Resto> m = Marcados();
            long total = 0;
            foreach (Resto r in m) if (r.Tamano > 0) total += r.Tamano;
            lblTotal.Text = m.Count + " de " + restos.Count + " marcados" + (total > 0 ? " · " + Util.FormatoTamano(total) : "");
            btnBorrar.Enabled = m.Count > 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.OscurecerTitulo(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tabla.ClearSelection();   // que no salga la primera fila resaltada como si estuviera elegida
        }

        /// <summary>Devuelve los restos que el usuario quiere borrar, o null si no quiere borrar nada.</summary>
        public static List<Resto> Elegir(IWin32Window dueno, string programa, List<Resto> restos)
        {
            if (EleccionAutomatica != null) return EleccionAutomatica(restos);
            using (VentanaRestos v = new VentanaRestos("Restos de " + programa,
                "Esto es lo que quedó en tu PC. Marca lo que quieras borrar: las carpetas y archivos van a la papelera " +
                "y de cada clave del registro se guarda antes una copia de seguridad.", restos, false, null))
            {
                DialogResult r = dueno != null ? v.ShowDialog(dueno) : v.ShowDialog();
                if (r != DialogResult.OK) return null;
                List<Resto> m = v.Marcados();
                return m.Count > 0 ? m : null;
            }
        }

        /// <summary>Informe final: qué se borró y qué no, uno por uno.</summary>
        public static void Informe(IWin32Window dueno, string programa, List<Resto> restos, string carpetaRespaldo)
        {
            Sonidos.Exito();
            if (EleccionAutomatica != null || Dialogo.RespuestaAutomatica != null) return;   // pruebas automáticas
            int fallos = restos.FindAll(r => !r.Borrado).Count;
            using (VentanaRestos v = new VentanaRestos(
                fallos == 0 ? "Listo: se borraron todos los restos de " + programa : "Se borraron casi todos los restos de " + programa,
                fallos == 0 ? "Esto es todo lo que se ha borrado. Las carpetas están en la papelera por si necesitas recuperar algo."
                            : "Lo que tiene ✗ no se pudo borrar (normalmente porque está en uso). Puedes reiniciar el PC y volver a intentarlo.",
                restos, true, carpetaRespaldo))
            {
                if (dueno != null) v.ShowDialog(dueno); else v.ShowDialog();
            }
        }
    }
}
