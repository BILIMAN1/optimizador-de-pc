using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Modo juego: cierra programas de fondo y sube el rendimiento; al desactivarlo deja todo como estaba.</summary>
    class PaginaModoJuego : PaginaBase
    {
        private readonly Label lblIcono, lblTitulo, lblDescripcion, lblPlan, lblLista;
        private readonly Boton btnActivar, btnActualizar;
        private readonly DataGridView tabla;
        private bool ocupado, cargando;

        public PaginaModoJuego() : base("Modo juego", "Más rendimiento para jugar, sin cerrar Discord ni los navegadores")
        {
            btnActualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnActualizar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnActualizar.Click += delegate { Cargar(); };
            Acciones.Controls.Add(btnActualizar);

            // ---- Tarjeta de estado ----
            Tarjeta estado = new Tarjeta();
            estado.Dock = DockStyle.Fill;
            estado.Margin = new Padding(0);
            estado.Padding = Tema.S(24, 20, 24, 20);
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 3;
            t.RowCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Tema.S(76)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lblIcono = Tema.Icono(Tema.IcoJuego, 30f, Tema.TextoSuave);
            lblIcono.Anchor = AnchorStyles.Left;
            FlowLayoutPanel textos = new FlowLayoutPanel();
            textos.Dock = DockStyle.Fill;
            textos.FlowDirection = FlowDirection.TopDown;
            textos.WrapContents = false;
            lblTitulo = Tema.Etiqueta("Modo juego desactivado", 15f, Tema.Texto, true);
            lblDescripcion = Tema.Etiqueta("", 9.5f, Tema.TextoSuave);
            lblDescripcion.MaximumSize = new Size(Tema.S(560), 0);
            lblDescripcion.Margin = new Padding(0, Tema.S(4), 0, 0);
            lblPlan = Tema.Etiqueta("", 9f, Tema.TextoTenue);
            lblPlan.Margin = new Padding(0, Tema.S(8), 0, 0);
            textos.Controls.Add(lblTitulo);
            textos.Controls.Add(lblDescripcion);
            textos.Controls.Add(lblPlan);
            btnActivar = new Boton("Activar modo juego", EstiloBoton.Primario, Tema.IcoPlay);
            btnActivar.Height = Tema.S(42);
            btnActivar.Anchor = AnchorStyles.Right;
            btnActivar.Margin = new Padding(0);
            btnActivar.Click += delegate { Alternar(); };
            t.Controls.Add(lblIcono, 0, 0);
            t.Controls.Add(textos, 1, 0);
            t.Controls.Add(btnActivar, 2, 0);
            estado.Controls.Add(t);

            // ---- Aviso de lo que nunca se toca ----
            Label nunca = Tema.Etiqueta("Nunca se cierran: Discord, navegadores, Riot y Vanguard, Steam, tus juegos, drivers ni nada de Windows.",
                9.5f, Tema.TextoSuave);
            nunca.Dock = DockStyle.Fill;
            nunca.AutoSize = false;
            nunca.TextAlign = ContentAlignment.MiddleLeft;
            nunca.Padding = new Padding(Tema.S(4), 0, 0, 0);

            // ---- Lista de programas ----
            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = false;
            DataGridViewCheckBoxColumn marca = new DataGridViewCheckBoxColumn();
            marca.Width = Tema.S(52);
            marca.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            marca.SortMode = DataGridViewColumnSortMode.NotSortable;
            tabla.Columns.Add(marca);
            tabla.Columns.Add(Columna("Programa", 30, false));
            tabla.Columns.Add(Columna("Por qué cerrarlo", 50, false));
            tabla.Columns.Add(Columna("Memoria", 16, true));
            tabla.Columns[1].DefaultCellStyle.Font = Tema.FuenteSemibold(9.5f);
            tabla.Columns[2].DefaultCellStyle.ForeColor = Tema.TextoSuave;

            lblLista = Tema.Etiqueta("Buscando programas abiertos…", 10f, Tema.TextoSuave);
            lblLista.AutoSize = false;
            lblLista.Dock = DockStyle.Fill;
            lblLista.TextAlign = ContentAlignment.MiddleCenter;

            Tarjeta lista = new Tarjeta();
            lista.Dock = DockStyle.Fill;
            lista.Margin = new Padding(0);
            lista.Padding = Tema.S(12, 6, 12, 12);
            lista.Controls.Add(tabla);
            lista.Controls.Add(lblLista);

            TableLayoutPanel cuerpo = new TableLayoutPanel();
            cuerpo.Dock = DockStyle.Fill;
            cuerpo.ColumnCount = 1;
            cuerpo.RowCount = 3;
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(150)));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(44)));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cuerpo.Controls.Add(estado, 0, 0);
            cuerpo.Controls.Add(nunca, 0, 1);
            cuerpo.Controls.Add(lista, 0, 2);
            Contenido.Controls.Add(cuerpo);
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

        public override void AlMostrar()
        {
            // Solo mira qué programas hay abiertos y el estado del modo juego
            Cargar();
        }

        private async void Cargar()
        {
            if (cargando || ocupado) return;
            cargando = true;
            btnActualizar.Enabled = false;
            try
            {
                bool activo = ModoJuego.Activo;
                List<ProgramaFondo> lista = null;
                string plan = null;
                await Task.Run(delegate
                {
                    plan = ModoJuego.NombrePlanActual();
                    if (!activo) lista = ModoJuego.Candidatos();
                });
                if (IsDisposed) return;
                MostrarEstado(activo, plan);
                if (activo) MostrarCerrados();
                else Llenar(lista);
            }
            catch (Exception ex)
            {
                Util.Log("Modo juego: error al leer los programas: " + ex.Message);
            }
            finally
            {
                cargando = false;
                if (!IsDisposed) btnActualizar.Enabled = true;
            }
        }

        private void MostrarEstado(bool activo, string plan)
        {
            lblIcono.ForeColor = activo ? Tema.Exito : Tema.TextoSuave;
            lblTitulo.Text = activo ? "Modo juego activado" : "Modo juego desactivado";
            lblTitulo.ForeColor = activo ? Tema.Exito : Tema.Texto;
            lblDescripcion.Text = activo
                ? "Tu PC está preparado para jugar. Cuando termines, desactívalo y todo volverá a estar como antes."
                : "Cierra los programas de fondo que marques, pone el plan de energía en Alto rendimiento y activa el Modo de juego de Windows.";
            lblPlan.Text = "Plan de energía actual: " + plan;
            btnActivar.Text = activo ? "Desactivar y dejarlo como estaba" : "Activar modo juego";
            btnActivar.Estilo = activo ? EstiloBoton.Secundario : EstiloBoton.Primario;
            btnActivar.Icono = activo ? Tema.IcoCancelar : Tema.IcoPlay;
        }

        private void Llenar(List<ProgramaFondo> lista)
        {
            tabla.Rows.Clear();
            foreach (ProgramaFondo f in lista)
            {
                int i = tabla.Rows.Add(f.Recomendado, f.Nombre, f.Motivo, Util.FormatoTamano(f.Ram));
                tabla.Rows[i].Tag = f;
            }
            tabla.Columns[0].ReadOnly = false;
            tabla.ClearSelection();
            tabla.Visible = lista.Count > 0;
            lblLista.Visible = lista.Count == 0;
            lblLista.Text = "No hay programas de fondo que merezca la pena cerrar. ¡Ya está listo para jugar!";
        }

        private void MostrarCerrados()
        {
            List<string> cerrados = ModoJuego.CerradosAlActivar();
            tabla.Visible = false;
            lblLista.Visible = true;
            StringBuilder sb = new StringBuilder("El modo juego está activo.");
            if (cerrados.Count > 0)
            {
                sb.Append("\n\nAl desactivarlo se volverán a abrir: ");
                for (int i = 0; i < cerrados.Count; i++) sb.Append(i > 0 ? ", " : "").Append(cerrados[i].Split('\t')[0]);
                sb.Append(".");
            }
            lblLista.Text = sb.ToString();
        }

        private List<ProgramaFondo> Marcados()
        {
            List<ProgramaFondo> l = new List<ProgramaFondo>();
            foreach (DataGridViewRow r in tabla.Rows)
            {
                object v = r.Cells[0].Value;
                if (v is bool && (bool)v && r.Tag is ProgramaFondo) l.Add((ProgramaFondo)r.Tag);
            }
            return l;
        }

        private async void Alternar()
        {
            if (ocupado || cargando) return;
            bool activo = ModoJuego.Activo;
            List<ProgramaFondo> cerrar = activo ? null : Marcados();
            if (!activo)
            {
                string msg = "Se pondrá el plan de energía en Alto rendimiento y se activará el Modo de juego de Windows.";
                if (cerrar.Count > 0)
                {
                    StringBuilder sb = new StringBuilder("\n\nSe cerrarán:\n");
                    foreach (ProgramaFondo f in cerrar) sb.Append("•  ").Append(f.Nombre).Append('\n');
                    msg += sb + "\nGuarda lo que tengas abierto en ellos. Discord y los navegadores no se tocan.";
                }
                if (!Dialogo.Confirmar(FindForm(), "¿Activar el modo juego?", msg, "Activar", false)) return;
            }

            ocupado = true;
            btnActivar.Enabled = false;
            btnActualizar.Enabled = false;
            lblDescripcion.Text = activo ? "Dejándolo todo como estaba…" : "Preparando el PC para jugar…";
            try
            {
                List<string> hecho = await Task.Run(() => activo ? ModoJuego.Desactivar() : ModoJuego.Activar(cerrar));
                if (IsDisposed) return;
                StringBuilder sb = new StringBuilder();
                foreach (string h in hecho) sb.Append("•  ").Append(h).Append('\n');
                Dialogo.Exito(FindForm(), activo ? "Modo juego desactivado" : "¡Modo juego activado!",
                    (sb.Length > 0 ? sb.ToString() : "Listo.") + (activo ? "" : "\n¡Buena partida!"));
            }
            catch (Exception ex)
            {
                Util.Log("Modo juego: error: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "Algo salió mal", "No se pudo cambiar el modo juego. El detalle está en el registro de la app.");
            }
            finally
            {
                ocupado = false;
                if (!IsDisposed)
                {
                    btnActivar.Enabled = true;
                    Cargar();
                }
            }
        }
    }
}
