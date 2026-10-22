using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Archivos repetidos en tus carpetas: se conserva una copia de cada uno y el resto puede ir a la papelera.</summary>
    class PaginaDuplicados : PaginaBase
    {
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        private class Fila
        {
            public FileInfo Archivo;
            public GrupoDuplicado Grupo;
        }

        private readonly DataGridView tabla;
        private readonly Label lblResumen, lblSeleccion, lblEstado;
        private readonly BarraUso barra;
        private readonly Boton btnBuscar, btnCancelar, btnAbrir, btnBorrar;
        private readonly Boton[] botonesMinimo;
        private readonly long[] minimos = { 1L << 20, 10L << 20, 100L << 20 };
        private int minimoElegido;
        private List<GrupoDuplicado> grupos = new List<GrupoDuplicado>();
        private CancellationTokenSource cancelar;
        private bool buscando, borrando, llenando;

        public PaginaDuplicados() : base("Duplicados", "Archivos repetidos en tus carpetas que ocupan espacio dos veces")
        {
            btnBuscar = new Boton("Buscar duplicados", EstiloBoton.Primario, Tema.IcoBuscar);
            btnBuscar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnBuscar.Click += delegate { Buscar(); };
            Acciones.Controls.Add(btnBuscar);

            // ---- Fila de opciones ----
            FlowLayoutPanel opciones = new FlowLayoutPanel();
            opciones.Dock = DockStyle.Fill;
            opciones.WrapContents = false;
            Label lm = Tema.Etiqueta("Tamaño mínimo:", 9.5f, Tema.TextoSuave);
            lm.Margin = new Padding(0, Tema.S(10), Tema.S(10), 0);
            opciones.Controls.Add(lm);
            string[] textos = { "1 MB", "10 MB", "100 MB" };
            botonesMinimo = new Boton[textos.Length];
            for (int i = 0; i < textos.Length; i++)
            {
                int indice = i;
                Boton b = new Boton(textos[i], EstiloBoton.Secundario);
                b.Click += delegate { if (!buscando && !borrando) { minimoElegido = indice; PintarMinimos(); } };
                botonesMinimo[i] = b;
                opciones.Controls.Add(b);
            }
            lblResumen = Tema.Etiqueta("", 10f, Tema.Texto, true);
            lblResumen.Anchor = AnchorStyles.Right;
            TableLayoutPanel arriba = new TableLayoutPanel();
            arriba.Dock = DockStyle.Fill;
            arriba.ColumnCount = 2;
            arriba.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            arriba.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            arriba.Controls.Add(opciones, 0, 0);
            arriba.Controls.Add(lblResumen, 1, 0);

            // ---- Tabla ----
            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = false;
            tabla.AlternatingRowsDefaultCellStyle.BackColor = Tema.Tarjeta;
            DataGridViewCheckBoxColumn marca = new DataGridViewCheckBoxColumn();
            marca.Width = Tema.S(52);
            marca.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            marca.SortMode = DataGridViewColumnSortMode.NotSortable;
            tabla.Columns.Add(marca);
            tabla.Columns.Add(Columna("Archivo", 30, false));
            tabla.Columns.Add(Columna("Carpeta", 40, false));
            tabla.Columns.Add(Columna("Tamaño", 12, true));
            tabla.Columns.Add(Columna("Fecha", 14, false));
            tabla.Columns[2].DefaultCellStyle.ForeColor = Tema.TextoTenue;
            tabla.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e) { if (!llenando && e.ColumnIndex == 0) ActualizarSeleccion(); };
            tabla.SelectionChanged += delegate { ActualizarBotones(); };
            tabla.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) AbrirUbicacion(); };

            lblEstado = Tema.Etiqueta("Pulsa «Buscar duplicados» para revisar tus carpetas.\nNo se borra nada sin que tú lo confirmes.", 10.5f, Tema.TextoSuave);
            lblEstado.AutoSize = false;
            lblEstado.Dock = DockStyle.Fill;
            lblEstado.TextAlign = ContentAlignment.MiddleCenter;

            barra = new BarraUso();
            barra.Dock = DockStyle.Top;
            barra.Visible = false;

            Tarjeta tarjeta = new Tarjeta();
            tarjeta.Dock = DockStyle.Fill;
            tarjeta.Margin = new Padding(0);
            tarjeta.Padding = Tema.S(12, 6, 12, 12);
            tarjeta.Controls.Add(tabla);
            tarjeta.Controls.Add(lblEstado);
            tarjeta.Controls.Add(barra);
            tabla.Visible = false;

            // ---- Barra inferior ----
            lblSeleccion = Tema.Etiqueta("", 9.5f, Tema.TextoSuave);
            lblSeleccion.Anchor = AnchorStyles.Left;
            btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario, Tema.IcoCancelar);
            btnCancelar.Visible = false;
            btnCancelar.Click += delegate { if (cancelar != null) cancelar.Cancel(); };
            btnAbrir = new Boton("Abrir ubicación", EstiloBoton.Fantasma, Tema.IcoArchivos);
            btnAbrir.Click += delegate { AbrirUbicacion(); };
            btnBorrar = new Boton("Enviar a la papelera", EstiloBoton.Peligro, Tema.IcoLimpieza);
            btnBorrar.Margin = new Padding(0);
            btnBorrar.Click += delegate { Borrar(); };
            TableLayoutPanel abajo = new TableLayoutPanel();
            abajo.Dock = DockStyle.Fill;
            abajo.ColumnCount = 4;
            abajo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) abajo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            abajo.Controls.Add(lblSeleccion, 0, 0);
            abajo.Controls.Add(btnCancelar, 1, 0);
            abajo.Controls.Add(btnAbrir, 2, 0);
            abajo.Controls.Add(btnBorrar, 3, 0);

            TableLayoutPanel cuerpo = new TableLayoutPanel();
            cuerpo.Dock = DockStyle.Fill;
            cuerpo.ColumnCount = 1;
            cuerpo.RowCount = 3;
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(52)));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(56)));
            cuerpo.Controls.Add(arriba, 0, 0);
            cuerpo.Controls.Add(tarjeta, 0, 1);
            cuerpo.Controls.Add(abajo, 0, 2);
            Contenido.Controls.Add(cuerpo);

            PintarMinimos();
            ActualizarSeleccion();
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

        private void PintarMinimos()
        {
            for (int i = 0; i < botonesMinimo.Length; i++) botonesMinimo[i].Estilo = i == minimoElegido ? EstiloBoton.Primario : EstiloBoton.Secundario;
        }

        public override void AlMostrar()
        {
            // No busca solo: comparar el contenido de archivos grandes lleva un rato y prefiere pedirlo el usuario
        }

        // ------------------------------------------------------------------ búsqueda (solo lectura)

        private async void Buscar()
        {
            if (buscando || borrando) return;
            buscando = true;
            cancelar = new CancellationTokenSource();
            CancellationToken ct = cancelar.Token;
            long minimo = minimos[minimoElegido];
            tabla.Visible = false;
            lblEstado.Visible = true;
            lblEstado.Text = "Buscando archivos repetidos…";
            barra.Visible = true;
            barra.Indeterminado = true;
            btnCancelar.Visible = true;
            ActualizarBotones();
            DateTime ultimo = DateTime.MinValue;
            Action<string> progreso = delegate(string t)
            {
                if ((DateTime.Now - ultimo).TotalMilliseconds < 120) return;
                ultimo = DateTime.Now;
                try { BeginInvoke(new Action(delegate { if (buscando) lblEstado.Text = t; })); } catch { }
            };
            try
            {
                List<GrupoDuplicado> r = await Task.Run(() => Duplicados.Buscar(Duplicados.CarpetasDeBusqueda(), minimo, ct, progreso));
                if (IsDisposed) return;
                grupos = r;
                Llenar();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) lblEstado.Text = "Búsqueda cancelada.";
            }
            catch (Exception ex)
            {
                Util.Log("Duplicados: error al buscar: " + ex);
                if (!IsDisposed) lblEstado.Text = "No se pudo terminar la búsqueda. Inténtalo otra vez.";
            }
            finally
            {
                buscando = false;
                if (!IsDisposed)
                {
                    barra.Indeterminado = false;
                    barra.Visible = false;
                    btnCancelar.Visible = false;
                    btnBuscar.Text = "Buscar de nuevo";
                    ActualizarSeleccion();
                }
            }
        }

        private void Llenar()
        {
            llenando = true;
            try
            {
                tabla.Rows.Clear();
                string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                List<DataGridViewRow> filas = new List<DataGridViewRow>();
                for (int gi = 0; gi < grupos.Count; gi++)
                {
                    GrupoDuplicado g = grupos[gi];
                    Color fondo = gi % 2 == 0 ? Tema.Tarjeta : Tema.FilaAlterna;
                    for (int i = 0; i < g.Archivos.Count; i++)
                    {
                        FileInfo f = g.Archivos[i];
                        string carpeta = f.DirectoryName ?? "";
                        if (carpeta.StartsWith(perfil, StringComparison.OrdinalIgnoreCase)) carpeta = "~" + carpeta.Substring(perfil.Length);
                        DataGridViewRow r = (DataGridViewRow)tabla.RowTemplate.Clone();
                        // Se conserva la copia más antigua (la primera) y se marcan las demás
                        r.CreateCells(tabla, i > 0, f.Name, carpeta, Util.FormatoTamano(g.Tamano), f.LastWriteTime.ToString("dd/MM/yyyy", Es));
                        r.DefaultCellStyle.BackColor = fondo;
                        if (i == 0) r.Cells[1].Style.ForeColor = Tema.Exito;
                        r.Cells[1].ToolTipText = i == 0 ? "Copia que se conserva (la más antigua)" : f.FullName;
                        Fila tag = new Fila();
                        tag.Archivo = f;
                        tag.Grupo = g;
                        r.Tag = tag;
                        filas.Add(r);
                    }
                }
                tabla.Rows.AddRange(filas.ToArray());
                tabla.ClearSelection();
            }
            finally
            {
                llenando = false;
            }
            long sobrante = 0;
            foreach (GrupoDuplicado g in grupos) sobrante += g.Sobrante;
            lblResumen.Text = grupos.Count == 0 ? "" : grupos.Count + (grupos.Count == 1 ? " grupo" : " grupos") + " · puedes liberar " + Util.FormatoTamano(sobrante);
            tabla.Visible = grupos.Count > 0;
            lblEstado.Visible = grupos.Count == 0;
            lblEstado.Text = "No hay archivos repetidos de más de " + botonesMinimo[minimoElegido].Text + ". ¡Bien!";
            ActualizarSeleccion();
        }

        // ------------------------------------------------------------------ selección y botones

        private List<Fila> Marcadas()
        {
            List<Fila> l = new List<Fila>();
            foreach (DataGridViewRow r in tabla.Rows)
            {
                object v = r.Cells[0].Value;
                if (v is bool && (bool)v && r.Tag is Fila) l.Add((Fila)r.Tag);
            }
            return l;
        }

        private void ActualizarSeleccion()
        {
            List<Fila> m = Marcadas();
            long bytes = 0;
            foreach (Fila f in m) bytes += f.Grupo.Tamano;
            lblSeleccion.Text = m.Count == 0 ? (grupos.Count > 0 ? "Marca las copias que quieras quitar" : "")
                : m.Count + (m.Count == 1 ? " archivo marcado · " : " archivos marcados · ") + Util.FormatoTamano(bytes);
            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            bool libre = !buscando && !borrando;
            btnBuscar.Enabled = libre;
            foreach (Boton b in botonesMinimo) b.Enabled = libre;
            btnBorrar.Enabled = libre && Marcadas().Count > 0;
            btnAbrir.Enabled = libre && tabla.Visible && tabla.SelectedRows.Count > 0;
        }

        private void AbrirUbicacion()
        {
            if (tabla.SelectedRows.Count == 0) return;
            Fila f = tabla.SelectedRows[0].Tag as Fila;
            if (f != null) Util.AbrirUbicacion(f.Archivo.FullName);
        }

        // ------------------------------------------------------------------ borrar (a la papelera)

        private async void Borrar()
        {
            List<Fila> marcadas = Marcadas();
            if (marcadas.Count == 0 || buscando || borrando) return;

            // Nunca se borran todas las copias de un archivo
            foreach (GrupoDuplicado g in grupos)
            {
                int quedan = g.Archivos.Count;
                foreach (Fila f in marcadas) if (f.Grupo == g) quedan--;
                if (quedan <= 0)
                {
                    Dialogo.Info(FindForm(), "Deja al menos una copia",
                        "Has marcado todas las copias de «" + g.Archivos[0].Name + "». Desmarca la que quieras conservar.");
                    return;
                }
            }
            long bytes = 0;
            foreach (Fila f in marcadas) bytes += f.Grupo.Tamano;
            if (!Dialogo.Confirmar(FindForm(), "¿Enviar " + marcadas.Count + (marcadas.Count == 1 ? " copia" : " copias") + " a la papelera?",
                "Irán a la papelera " + Util.FormatoTamano(bytes) + " en archivos repetidos. De cada uno se conserva al menos una copia.\n\n" +
                "Cuando vacíes la papelera se liberará el espacio.", "Enviar a la papelera", true)) return;

            borrando = true;
            ActualizarBotones();
            int ok = 0;
            List<string> fallos = new List<string>();
            try
            {
                await Task.Run(delegate
                {
                    foreach (Fila f in marcadas)
                    {
                        try
                        {
                            // Se comprueba justo antes: sigue igual y queda otra copia idéntica en su sitio
                            FileInfo fi = new FileInfo(f.Archivo.FullName);
                            if (!fi.Exists || fi.Length != f.Grupo.Tamano) { fallos.Add(f.Archivo.Name + ": ha cambiado"); continue; }
                            string huella = Duplicados.HuellaCompleta(fi);
                            bool quedaCopia = false;
                            foreach (FileInfo otra in f.Grupo.Archivos)
                            {
                                if (string.Equals(otra.FullName, fi.FullName, StringComparison.OrdinalIgnoreCase)) continue;
                                if (marcadas.Exists(x => string.Equals(x.Archivo.FullName, otra.FullName, StringComparison.OrdinalIgnoreCase))) continue;
                                FileInfo o = new FileInfo(otra.FullName);
                                if (o.Exists && huella != null && huella == Duplicados.HuellaCompleta(o)) { quedaCopia = true; break; }
                            }
                            if (!quedaCopia) { fallos.Add(f.Archivo.Name + ": no queda otra copia igual"); continue; }
                            Exception e = Residuos.MoverAPapelera(fi.FullName, false);
                            if (e != null || File.Exists(fi.FullName)) { fallos.Add(f.Archivo.Name + ": está en uso"); continue; }
                            Util.Log("Duplicados: a la papelera " + fi.FullName);
                            ok++;
                        }
                        catch (Exception ex) { fallos.Add(f.Archivo.Name + ": " + ex.Message); }
                    }
                });
                if (IsDisposed) return;

                // Quitar de la lista lo que ya no está y los grupos que se quedan con una sola copia
                foreach (GrupoDuplicado g in grupos) g.Archivos.RemoveAll(x => !File.Exists(x.FullName));
                grupos.RemoveAll(g => g.Archivos.Count < 2);
                Llenar();

                if (fallos.Count == 0)
                {
                    Dialogo.Exito(FindForm(), "Listo", ok + (ok == 1 ? " copia enviada" : " copias enviadas") + " a la papelera. Vacíala para liberar el espacio.");
                }
                else
                {
                    string detalle = string.Join("\n", fallos.GetRange(0, Math.Min(6, fallos.Count)).ToArray());
                    Dialogo.Error(FindForm(), ok > 0 ? "Algunas copias no se movieron" : "No se movió ninguna copia",
                        (ok > 0 ? ok + " sí se enviaron a la papelera. Estas no:\n\n" : "") + detalle);
                }
            }
            finally
            {
                borrando = false;
                if (!IsDisposed) ActualizarSeleccion();
            }
        }
    }
}
