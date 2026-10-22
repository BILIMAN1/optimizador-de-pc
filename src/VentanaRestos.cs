using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Muestra los restos encontrados tras desinstalar y deja elegir cuáles borrar.</summary>
    class VentanaRestos : Form
    {
        /// <summary>Solo para las pruebas automáticas: si no es null, decide sin mostrar la ventana.</summary>
        internal static Func<List<Resto>, List<Resto>> EleccionAutomatica;

        private readonly List<Resto> restos;
        private readonly DataGridView tabla;
        private readonly Label lblTotal;
        private readonly Boton btnBorrar;

        private VentanaRestos(string programa, List<Resto> restos)
        {
            this.restos = restos;
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
            ClientSize = new Size(Tema.S(820), Tema.S(520));
            Padding = new Padding(Tema.S(24), Tema.S(20), Tema.S(24), Tema.S(20));

            Label titulo = Tema.Etiqueta("Restos de " + programa, 14f, Tema.Texto, true);
            titulo.Dock = DockStyle.Top;
            titulo.AutoSize = false;
            titulo.AutoEllipsis = true;
            titulo.Height = Tema.S(34);

            Label detalle = Tema.Etiqueta("El desinstalador dejó estas cosas en tu PC. Lo marcado se borrará: las carpetas y archivos van a la " +
                "papelera y de cada clave del registro se guarda antes una copia de seguridad.", 9.5f, Tema.TextoSuave);
            detalle.Dock = DockStyle.Top;
            detalle.AutoSize = false;
            detalle.Height = Tema.S(46);

            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = false;
            DataGridViewCheckBoxColumn colMarca = new DataGridViewCheckBoxColumn();
            colMarca.Width = Tema.S(48);
            colMarca.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colMarca.SortMode = DataGridViewColumnSortMode.NotSortable;
            DataGridViewTextBoxColumn colTipo = Columna("Tipo", 18);
            DataGridViewTextBoxColumn colRuta = Columna("Ubicación", 62);
            colRuta.DefaultCellStyle.ForeColor = Tema.TextoSuave;
            DataGridViewTextBoxColumn colTam = Columna("Tamaño", 16);
            colTam.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colTam.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            tabla.Columns.AddRange(new DataGridViewColumn[] { colMarca, colTipo, colRuta, colTam });
            tabla.ShowCellToolTips = true;
            foreach (Resto r in restos)
            {
                int i = tabla.Rows.Add(r.Marcado, NombreTipo(r.Tipo), r.Ruta, r.Tamano >= 0 ? Util.FormatoTamano(r.Tamano) : "—");
                tabla.Rows[i].Tag = r;
                tabla.Rows[i].Cells[2].ToolTipText = r.Ruta + "\n" + r.Motivo;
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
            btnBorrar = new Boton("Borrar lo marcado", EstiloBoton.Peligro, Tema.IcoLimpieza);
            btnBorrar.DialogResult = DialogResult.OK;
            Boton btnNo = new Boton("No borrar nada", EstiloBoton.Secundario);
            btnNo.DialogResult = DialogResult.Cancel;
            btnBorrar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnNo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBorrar.Location = new Point(ClientSize.Width - Padding.Horizontal - btnBorrar.Width, Tema.S(18));
            btnNo.Location = new Point(btnBorrar.Left - Tema.S(8) - btnNo.Width, Tema.S(18));
            abajo.Controls.Add(lblTotal);
            abajo.Controls.Add(btnNo);
            abajo.Controls.Add(btnBorrar);
            CancelButton = btnNo;

            Panel separador = new Panel();
            separador.Dock = DockStyle.Top;
            separador.Height = Tema.S(6);

            Controls.Add(tarjeta);
            Controls.Add(separador);
            Controls.Add(detalle);
            Controls.Add(titulo);
            Controls.Add(abajo);
            tarjeta.BringToFront();
            ActualizarTotal();
        }

        private static DataGridViewTextBoxColumn Columna(string titulo, float peso)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
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
            foreach (DataGridViewRow r in tabla.Rows)
            {
                object v = r.Cells[0].Value;
                if (v is bool && (bool)v) l.Add((Resto)r.Tag);
            }
            return l;
        }

        private void ActualizarTotal()
        {
            List<Resto> m = Marcados();
            long bytes = 0;
            foreach (Resto r in m) if (r.Tamano > 0) bytes += r.Tamano;
            lblTotal.Text = m.Count + " de " + restos.Count + " marcados" + (bytes > 0 ? " · " + Util.FormatoTamano(bytes) : "");
            btnBorrar.Enabled = m.Count > 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.OscurecerTitulo(this);
        }

        /// <summary>Devuelve los restos que el usuario quiere borrar, o null si no quiere borrar nada.</summary>
        public static List<Resto> Elegir(IWin32Window dueno, string programa, List<Resto> restos)
        {
            if (EleccionAutomatica != null) return EleccionAutomatica(restos);
            using (VentanaRestos v = new VentanaRestos(programa, restos))
            {
                DialogResult r = dueno != null ? v.ShowDialog(dueno) : v.ShowDialog();
                if (r != DialogResult.OK) return null;
                List<Resto> m = v.Marcados();
                return m.Count > 0 ? m : null;
            }
        }
    }
}
