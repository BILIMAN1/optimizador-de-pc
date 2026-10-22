using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Cuánto espacio ha liberado la app en total y cada vez que limpió algo.</summary>
    class PaginaHistorial : PaginaBase
    {
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        private readonly Label lblTotal, lblTotalSub, lblMes, lblMesSub, lblUltima, lblUltimaSub, lblVacio;
        private readonly DataGridView tabla;
        private readonly Boton btnBorrar;
        private bool cargando;

        public PaginaHistorial() : base("Historial", "Todo el espacio que has liberado con el optimizador")
        {
            Boton btnActualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnActualizar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnActualizar.Click += delegate { Cargar(); };
            btnBorrar = new Boton("Borrar historial", EstiloBoton.Fantasma, Tema.IcoLimpieza);
            btnBorrar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnBorrar.Click += delegate { BorrarHistorial(); };
            Acciones.Controls.Add(btnActualizar);
            Acciones.Controls.Add(btnBorrar);

            // ---- Tarjetas de resumen ----
            TableLayoutPanel fila = new TableLayoutPanel();
            fila.Dock = DockStyle.Fill;
            fila.Margin = new Padding(0);
            fila.ColumnCount = 3;
            fila.RowCount = 1;
            for (int i = 0; i < 3; i++) fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fila.Controls.Add(NuevaTarjeta("Total liberado", Tema.IcoDisco, out lblTotal, out lblTotalSub, Tema.Acento, new Padding(0, 0, Tema.S(8), 0)), 0, 0);
            fila.Controls.Add(NuevaTarjeta("Este mes", "", out lblMes, out lblMesSub, Tema.Texto, new Padding(Tema.S(8), 0, Tema.S(8), 0)), 1, 0);
            fila.Controls.Add(NuevaTarjeta("Última limpieza", Tema.IcoHistorial, out lblUltima, out lblUltimaSub, Tema.Texto, new Padding(Tema.S(8), 0, 0, 0)), 2, 0);

            // ---- Tabla ----
            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = true;
            tabla.Columns.Add(Columna("Fecha", 22, DataGridViewContentAlignment.MiddleLeft));
            tabla.Columns.Add(Columna("Qué se limpió", 38, DataGridViewContentAlignment.MiddleLeft));
            tabla.Columns.Add(Columna("Cómo", 24, DataGridViewContentAlignment.MiddleLeft));
            tabla.Columns.Add(Columna("Espacio", 16, DataGridViewContentAlignment.MiddleRight));
            tabla.Columns[0].DefaultCellStyle.ForeColor = Tema.TextoSuave;
            tabla.Columns[2].DefaultCellStyle.ForeColor = Tema.TextoSuave;
            tabla.Columns[3].DefaultCellStyle.Font = Tema.FuenteSemibold(9.5f);

            lblVacio = Tema.Etiqueta("Todavía no has limpiado nada con el optimizador.\nCuando lo hagas, aquí verás cuánto espacio has ganado.", 10.5f, Tema.TextoSuave);
            lblVacio.AutoSize = false;
            lblVacio.Dock = DockStyle.Fill;
            lblVacio.TextAlign = ContentAlignment.MiddleCenter;
            lblVacio.Visible = false;

            Tarjeta tarjetaTabla = new Tarjeta();
            tarjetaTabla.Dock = DockStyle.Fill;
            tarjetaTabla.Margin = new Padding(0);
            tarjetaTabla.Padding = Tema.S(12, 6, 12, 12);
            tarjetaTabla.Controls.Add(tabla);
            tarjetaTabla.Controls.Add(lblVacio);

            TableLayoutPanel cuerpo = new TableLayoutPanel();
            cuerpo.Dock = DockStyle.Fill;
            cuerpo.Margin = new Padding(0);
            cuerpo.ColumnCount = 1;
            cuerpo.RowCount = 3;
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(124)));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(16)));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cuerpo.Controls.Add(fila, 0, 0);
            cuerpo.Controls.Add(tarjetaTabla, 0, 2);
            Contenido.Controls.Add(cuerpo);
        }

        private static Tarjeta NuevaTarjeta(string titulo, string icono, out Label valor, out Label sub, Color colorValor, Padding margen)
        {
            Tarjeta t = new Tarjeta();
            t.Dock = DockStyle.Fill;
            t.Margin = margen;
            t.Padding = Tema.S(20, 16, 20, 14);
            Label ico = Tema.Icono(icono, 12f, Tema.Acento);
            ico.Location = new Point(Tema.S(20), Tema.S(18));
            Label lt = Tema.Etiqueta(titulo, 10f, Tema.TextoSuave);
            lt.Location = new Point(Tema.S(46), Tema.S(17));
            valor = Tema.Etiqueta("—", 20f, colorValor, true);
            valor.Location = new Point(Tema.S(18), Tema.S(44));
            sub = Tema.Etiqueta("", 9f, Tema.TextoTenue);
            sub.Location = new Point(Tema.S(20), Tema.S(88));
            t.Controls.Add(ico);
            t.Controls.Add(lt);
            t.Controls.Add(valor);
            t.Controls.Add(sub);
            return t;
        }

        private static DataGridViewTextBoxColumn Columna(string titulo, float peso, DataGridViewContentAlignment alineacion)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.DefaultCellStyle.Alignment = alineacion;
            c.HeaderCell.Style.Alignment = alineacion;
            return c;
        }

        public override void AlMostrar()
        {
            // Solo lee el archivo del historial; se recarga cada vez para ver las últimas limpiezas
            Cargar();
        }

        private async void Cargar()
        {
            if (cargando) return;
            cargando = true;
            try
            {
                List<EntradaHistorial> l = await Task.Run(() => Historial.Leer());
                if (IsDisposed) return;
                Mostrar(l);
            }
            catch (Exception ex)
            {
                Util.Log("Historial: no se pudo leer: " + ex.Message);
            }
            finally
            {
                cargando = false;
            }
        }

        private void Mostrar(List<EntradaHistorial> l)
        {
            long total = 0, mes = 0;
            int limpiezasMes = 0;
            DateTime hoy = DateTime.Now;
            DateTime primera = DateTime.MaxValue;
            foreach (EntradaHistorial e in l)
            {
                total += e.Bytes;
                if (e.Fecha < primera) primera = e.Fecha;
                if (e.Fecha.Year == hoy.Year && e.Fecha.Month == hoy.Month) { mes += e.Bytes; limpiezasMes++; }
            }

            lblTotal.Text = Util.FormatoTamano(total);
            lblTotalSub.Text = l.Count == 0 ? "Aún no hay limpiezas" : "Desde el " + primera.ToString("d 'de' MMMM 'de' yyyy", Es);
            lblMes.Text = Util.FormatoTamano(mes);
            lblMesSub.Text = limpiezasMes == 1 ? "1 cosa limpiada" : limpiezasMes + " cosas limpiadas";
            if (l.Count > 0)
            {
                lblUltima.Text = Cuando(l[0].Fecha);
                lblUltimaSub.Text = Util.Recortar(l[0].Detalle, 34) + " · " + Util.FormatoTamano(l[0].Bytes);
            }
            else
            {
                lblUltima.Text = "Nunca";
                lblUltimaSub.Text = "";
            }

            tabla.Rows.Clear();
            List<DataGridViewRow> filas = new List<DataGridViewRow>();
            foreach (EntradaHistorial e in l)
            {
                DataGridViewRow r = (DataGridViewRow)tabla.RowTemplate.Clone();
                r.CreateCells(tabla, e.Fecha.ToString("dd/MM/yyyy  HH:mm", Es), e.Detalle, e.Origen, Util.FormatoTamano(e.Bytes));
                filas.Add(r);
            }
            tabla.Rows.AddRange(filas.ToArray());
            tabla.ClearSelection();
            tabla.Visible = l.Count > 0;
            lblVacio.Visible = l.Count == 0;
            btnBorrar.Enabled = l.Count > 0;
        }

        private static string Cuando(DateTime f)
        {
            DateTime hoy = DateTime.Today;
            if (f.Date == hoy) return "Hoy, " + f.ToString("HH:mm", Es);
            if (f.Date == hoy.AddDays(-1)) return "Ayer, " + f.ToString("HH:mm", Es);
            int dias = (int)(hoy - f.Date).TotalDays;
            if (dias < 7) return "Hace " + dias + " días";
            return f.ToString("d MMM yyyy", Es);
        }

        private void BorrarHistorial()
        {
            if (!Dialogo.Confirmar(FindForm(), "¿Borrar el historial?",
                "Se borrará solo la lista de limpiezas y el contador de espacio liberado. No afecta a nada de tu PC.",
                "Borrar historial", true)) return;
            Historial.Borrar();
            Util.Log("Historial borrado");
            Cargar();
        }
    }
}
