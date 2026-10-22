using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>
    /// Programas y tareas programadas que se abren solos al encender el PC.
    /// Apagar algo aquí solo lo marca como desactivado (igual que el Administrador de tareas).
    /// </summary>
    class PaginaInicio : PaginaBase
    {
        private const string IcoAbrirFuera = "";

        private readonly Tarjeta aviso;
        private readonly Label lblAvisoIcono, lblAviso, lblResumen;
        private readonly Boton btnProgramas, btnTareas, btnActualizar, btnAdministrador;
        private readonly Tarjeta tarjetaTabla;
        private readonly DataGridView tabla;
        private readonly DataGridViewCheckBoxColumn colActivo;
        private readonly DataGridViewTextBoxColumn colNombre, colOrigen, colComando, colNota;

        // Estado vacío / cargando / error, centrado dentro de la tarjeta de la tabla
        private readonly Panel panelEstado;
        private readonly Label lblEstadoIcono, lblEstado;
        private readonly BarraUso barraEstado;

        private List<ElementoInicio> programas = new List<ElementoInicio>();
        private List<ElementoInicio> tareas = new List<ElementoInicio>();
        private bool verTareas, vistaTareasMostrada, cargado, cargando, tareasListas;
        private bool ocupado, pendienteRellenar, ignorarCambios;
        private string errorCarga;

        public PaginaInicio() : base("Inicio de Windows", "Programas que se abren solos al encender el PC")
        {
            // ---- Botones del encabezado (el primero queda a la derecha) ----
            btnActualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnActualizar.Click += delegate { if (!ocupado) Cargar(); };
            btnAdministrador = new Boton("Abrir Administrador de tareas", EstiloBoton.Fantasma, IcoAbrirFuera);
            btnAdministrador.Click += delegate { AbrirAdministrador(); };
            Acciones.Controls.Add(btnActualizar);
            Acciones.Controls.Add(btnAdministrador);

            // ---- Aviso informativo ----
            aviso = new Tarjeta();
            aviso.Padding = new Padding(0);
            lblAvisoIcono = Tema.Icono(Tema.IcoInfo, 12f, Tema.Acento);
            lblAviso = Tema.Etiqueta("Apagar un programa aquí no lo desinstala: solo evita que se abra solo al encender el PC. " +
                "Es lo mismo que hace el Administrador de tareas y puedes volver a activarlo cuando quieras.", 9.5f, Tema.TextoSuave);
            lblAviso.AutoSize = false;
            aviso.Controls.Add(lblAvisoIcono);
            aviso.Controls.Add(lblAviso);

            // ---- Filtro (botones segmentados) y resumen ----
            btnProgramas = new Boton("Programas", EstiloBoton.Primario);
            btnProgramas.Click += delegate { CambiarVista(false); };
            btnTareas = new Boton("Tareas programadas", EstiloBoton.Secundario);
            btnTareas.Click += delegate { CambiarVista(true); };
            lblResumen = Tema.Etiqueta("", 9.5f, Tema.TextoSuave);

            // ---- Tabla ----
            tarjetaTabla = new Tarjeta();
            tarjetaTabla.Padding = Tema.S(14, 8, 14, 12);

            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ShowCellToolTips = true;

            colActivo = new DataGridViewCheckBoxColumn();
            colActivo.HeaderText = "Activo";
            colActivo.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colActivo.Width = Tema.S(76);
            colActivo.Resizable = DataGridViewTriState.False;
            colActivo.SortMode = DataGridViewColumnSortMode.NotSortable;
            colActivo.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            colNombre = ColumnaTexto("Nombre", 45);
            colNombre.MinimumWidth = Tema.S(120);

            colOrigen = ColumnaTexto("Origen", 0);
            colOrigen.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colOrigen.Width = Tema.S(150);
            colOrigen.DefaultCellStyle.ForeColor = Tema.TextoSuave;
            colOrigen.DefaultCellStyle.SelectionForeColor = Tema.TextoSuave;

            colComando = ColumnaTexto("Comando", 55);
            colComando.MinimumWidth = Tema.S(100);
            colComando.DefaultCellStyle.ForeColor = Tema.TextoTenue;
            colComando.DefaultCellStyle.SelectionForeColor = Tema.TextoSuave;
            colComando.DefaultCellStyle.Font = Tema.Fuente(9f);

            colNota = ColumnaTexto("Nota", 0);
            colNota.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colNota.Width = Tema.S(136);
            colNota.DefaultCellStyle.Font = Tema.Fuente(9f);
            colNota.DefaultCellStyle.ForeColor = Tema.TextoTenue;
            colNota.DefaultCellStyle.SelectionForeColor = Tema.TextoSuave;

            tabla.Columns.AddRange(new DataGridViewColumn[] { colActivo, colNombre, colOrigen, colComando, colNota });
            tabla.CellValueChanged += Tabla_CellValueChanged;
            tabla.CellClick += Tabla_CellClick;

            // ---- Estado (cargando / vacío / error) ----
            panelEstado = new Panel();
            panelEstado.Dock = DockStyle.Fill;
            lblEstadoIcono = Tema.Icono(Tema.IcoInicio, 22f, Tema.TextoTenue);
            lblEstado = Tema.Etiqueta("", 10.5f, Tema.TextoSuave);
            lblEstado.AutoSize = false;
            lblEstado.TextAlign = ContentAlignment.TopCenter;
            barraEstado = new BarraUso();
            barraEstado.Width = Tema.S(180);
            barraEstado.Height = Tema.S(6);
            panelEstado.Controls.Add(lblEstadoIcono);
            panelEstado.Controls.Add(lblEstado);
            panelEstado.Controls.Add(barraEstado);
            panelEstado.Resize += delegate { CentrarEstado(); };

            tarjetaTabla.Controls.Add(tabla);
            tarjetaTabla.Controls.Add(panelEstado);

            Contenido.Controls.Add(aviso);
            Contenido.Controls.Add(btnProgramas);
            Contenido.Controls.Add(btnTareas);
            Contenido.Controls.Add(lblResumen);
            Contenido.Controls.Add(tarjetaTabla);
            Contenido.Resize += delegate { Acomodar(); };

            MostrarEstado(Tema.IcoInicio, "Cargando…", true);
            Acomodar();
        }

        private static DataGridViewTextBoxColumn ColumnaTexto(string titulo, int peso)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (peso > 0) c.FillWeight = peso;
            return c;
        }

        // ------------------------------------------------------------------ diseño

        private void Acomodar()
        {
            int w = Contenido.ClientSize.Width, h = Contenido.ClientSize.Height;
            if (w <= 0) return;

            // Aviso: el texto se parte en varias líneas si no cabe
            int margenX = Tema.S(18), margenY = Tema.S(14);
            int xTexto = margenX + lblAvisoIcono.Width + Tema.S(12);
            int anchoTexto = Math.Max(Tema.S(120), w - xTexto - margenX);
            Size st = lblAviso.GetPreferredSize(new Size(anchoTexto, 0));
            lblAviso.SetBounds(xTexto, margenY, anchoTexto, st.Height);
            int altoAviso = Math.Max(st.Height, lblAvisoIcono.Height) + margenY * 2;
            lblAvisoIcono.Location = new Point(margenX, margenY + Math.Max(0, (Math.Min(st.Height, Tema.S(20)) - lblAvisoIcono.Height) / 2) + Tema.S(1));
            aviso.SetBounds(0, 0, w, altoAviso);

            // Fila del filtro
            int y = altoAviso + Tema.S(20);
            btnProgramas.Location = new Point(0, y);
            btnTareas.Location = new Point(btnProgramas.Right + Tema.S(8), y);
            lblResumen.Location = new Point(Math.Max(btnTareas.Right + Tema.S(16), w - lblResumen.Width - Tema.S(4)),
                y + (btnProgramas.Height - lblResumen.Height) / 2);
            y += btnProgramas.Height + Tema.S(16);

            tarjetaTabla.SetBounds(0, y, w, Math.Max(Tema.S(160), h - y));
        }

        private void CentrarEstado()
        {
            int w = panelEstado.ClientSize.Width, h = panelEstado.ClientSize.Height;
            if (w <= 0) return;
            int ancho = Math.Max(Tema.S(100), Math.Min(Tema.S(520), w - Tema.S(40)));
            Size st = lblEstado.GetPreferredSize(new Size(ancho, 0));
            lblEstado.Size = new Size(ancho, st.Height);

            int total = lblEstado.Height;
            if (lblEstadoIcono.Visible) total += lblEstadoIcono.Height + Tema.S(12);
            if (barraEstado.Visible) total += barraEstado.Height + Tema.S(18);
            int y = Math.Max(0, (h - total) / 2 - Tema.S(10));
            if (lblEstadoIcono.Visible)
            {
                lblEstadoIcono.Location = new Point((w - lblEstadoIcono.Width) / 2, y);
                y = lblEstadoIcono.Bottom + Tema.S(12);
            }
            lblEstado.Location = new Point((w - lblEstado.Width) / 2, y);
            y = lblEstado.Bottom + Tema.S(18);
            if (barraEstado.Visible) barraEstado.Location = new Point((w - barraEstado.Width) / 2, y);
        }

        private void MostrarEstado(string icono, string texto, bool animado)
        {
            lblEstadoIcono.Text = icono ?? "";
            lblEstadoIcono.Visible = !string.IsNullOrEmpty(icono);
            lblEstado.Text = texto;
            barraEstado.Visible = animado;
            barraEstado.Indeterminado = animado;
            tabla.Visible = false;
            panelEstado.Visible = true;
            panelEstado.BringToFront();
            CentrarEstado();
        }

        private void OcultarEstado()
        {
            barraEstado.Indeterminado = false;
            panelEstado.Visible = false;
            tabla.Visible = true;
        }

        // ------------------------------------------------------------------ datos

        public override void AlMostrar()
        {
            // Solo se carga la primera vez; después, con el botón Actualizar
            if (!cargado && !cargando) Cargar();
        }

        private async void Cargar()
        {
            if (cargando || ocupado) return;
            cargando = true;
            tareasListas = false;
            errorCarga = null;
            btnActualizar.Enabled = false;
            if (!cargado) MostrarEstado(Tema.IcoInicio, "Cargando…", true);
            else if (verTareas) MostrarEstado(Tema.IcoReloj, "Leyendo las tareas programadas…", true);
            ActualizarContadores();
            try
            {
                // 1) Programas (registro y carpeta Inicio): es instantáneo
                List<ElementoInicio> rapidos = await Task.Run(() => ElementosInicio.Obtener(false));
                if (IsDisposed) return;
                programas = Ordenar(rapidos.Where(e => e.Tipo != TipoInicio.Tarea));
                cargado = true;
                if (!verTareas) Rellenar();
                ActualizarContadores();

                // 2) Tareas programadas: tardan unos segundos
                List<ElementoInicio> todos = await Task.Run(() => ElementosInicio.Obtener(true));
                if (IsDisposed) return;
                tareas = Ordenar(todos.Where(e => e.Tipo == TipoInicio.Tarea));
                tareasListas = true;
                if (verTareas) Rellenar();
                ActualizarContadores();
            }
            catch (Exception ex)
            {
                Util.Log("Inicio: no se pudo leer la lista: " + ex);
                if (IsDisposed) return;
                errorCarga = "No se pudo leer la lista de programas. Prueba con «Actualizar».";
                tareasListas = true;
                Rellenar();
            }
            finally
            {
                cargando = false;
                if (!IsDisposed)
                {
                    btnActualizar.Enabled = !ocupado;
                    ActualizarContadores();
                }
            }
        }

        private static List<ElementoInicio> Ordenar(IEnumerable<ElementoInicio> lista)
        {
            // Primero los que están activos, luego por nombre
            return lista.OrderByDescending(e => e.Habilitado)
                        .ThenBy(e => e.Nombre ?? "", StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
        }

        private void CambiarVista(bool tareasVista)
        {
            if (ocupado || verTareas == tareasVista) return;
            verTareas = tareasVista;
            btnProgramas.Estilo = verTareas ? EstiloBoton.Secundario : EstiloBoton.Primario;
            btnTareas.Estilo = verTareas ? EstiloBoton.Primario : EstiloBoton.Secundario;
            if (cargado || errorCarga != null) Rellenar();
            ActualizarContadores();
        }

        private static string Clave(ElementoInicio e)
        {
            return e.Tipo + "|" + e.Origen + "|" + e.Nombre;
        }

        /// <summary>Vuelve a llenar la tabla con la vista actual, conservando selección y scroll si se puede.</summary>
        private void Rellenar()
        {
            if (ocupado) { pendienteRellenar = true; return; }

            if (errorCarga != null)
            {
                MostrarEstado(Tema.IcoAviso, errorCarga, false);
                return;
            }
            if (verTareas && !tareasListas)
            {
                MostrarEstado(Tema.IcoReloj, "Leyendo las tareas programadas…", true);
                return;
            }

            List<ElementoInicio> lista = verTareas ? tareas : programas;

            string claveSel = null;
            int primera = -1;
            if (vistaTareasMostrada == verTareas && tabla.Rows.Count > 0)
            {
                if (tabla.SelectedRows.Count > 0)
                {
                    ElementoInicio sel = tabla.SelectedRows[0].Tag as ElementoInicio;
                    if (sel != null) claveSel = Clave(sel);
                }
                primera = tabla.FirstDisplayedScrollingRowIndex;
            }

            ignorarCambios = true;
            try
            {
                tabla.Rows.Clear();
                List<DataGridViewRow> filas = new List<DataGridViewRow>();
                foreach (ElementoInicio e in lista)
                {
                    // Copia de la plantilla para heredar el alto de fila del tema
                    DataGridViewRow f = (DataGridViewRow)tabla.RowTemplate.Clone();
                    f.CreateCells(tabla, e.Habilitado, e.Nombre ?? "", e.Origen ?? "", e.Comando ?? "", Nota(e));
                    f.Tag = e;
                    f.Cells[colComando.Index].ToolTipText = e.Comando ?? "";
                    EstiloFila(f, e);
                    filas.Add(f);
                }
                if (filas.Count > 0) tabla.Rows.AddRange(filas.ToArray());

                tabla.ClearSelection();
                tabla.CurrentCell = null;
                if (claveSel != null)
                {
                    foreach (DataGridViewRow f in tabla.Rows)
                    {
                        ElementoInicio e = f.Tag as ElementoInicio;
                        if (e != null && Clave(e) == claveSel) { f.Selected = true; break; }
                    }
                }
                if (primera > 0 && primera < tabla.Rows.Count)
                {
                    try { tabla.FirstDisplayedScrollingRowIndex = primera; } catch { }
                }
            }
            finally
            {
                ignorarCambios = false;
            }
            vistaTareasMostrada = verTareas;

            if (lista.Count == 0)
            {
                MostrarEstado(Tema.IcoCheck, verTareas
                    ? "No hay tareas programadas de otros programas."
                    : "No hay programas que se abran solos al encender el PC.", false);
            }
            else
            {
                OcultarEstado();
            }
            ActualizarContadores();
        }

        private static string Nota(ElementoInicio e)
        {
            if (e.MejorMantener) return "Mejor no tocar";
            if (e.RequiereAdmin && !Util.EsAdmin) return "Necesita admin";
            return "";
        }

        private void EstiloFila(DataGridViewRow f, ElementoInicio e)
        {
            DataGridViewCell nombre = f.Cells[colNombre.Index];
            nombre.Style.ForeColor = e.Habilitado ? Tema.Texto : Tema.TextoSuave;
            nombre.Style.SelectionForeColor = e.Habilitado ? Tema.Texto : Tema.TextoSuave;

            DataGridViewCell nota = f.Cells[colNota.Index];
            if (e.MejorMantener)
            {
                nota.Style.ForeColor = Tema.Aviso;
                nota.Style.SelectionForeColor = Tema.Aviso;
                nota.ToolTipText = "Parece un driver, un antivirus o un anticheat: mejor dejarlo activo.";
            }
            else if (e.RequiereAdmin && !Util.EsAdmin)
            {
                nota.ToolTipText = "Abre el Optimizador como administrador para cambiarlo.";
            }

            // Sin permisos, la casilla se ve apagada; al pulsarla se explica por qué
            f.Cells[colActivo.Index].ReadOnly = e.RequiereAdmin && !Util.EsAdmin;
        }

        private void ActualizarContadores()
        {
            btnProgramas.Text = cargado ? "Programas (" + programas.Count + ")" : "Programas";
            btnTareas.Text = tareasListas && errorCarga == null ? "Tareas programadas (" + tareas.Count + ")" : "Tareas programadas";

            string texto = "";
            if (errorCarga == null)
            {
                if (!verTareas && cargado)
                {
                    int n = programas.Count, activos = programas.Count(e => e.Habilitado);
                    texto = n == 0 ? "Ningún programa se abre solo"
                        : string.Format("{0} de {1} {2} {3} al encender el PC", activos, n,
                            n == 1 ? "programa" : "programas", activos == 1 ? "se abre" : "se abren");
                }
                else if (verTareas && tareasListas)
                {
                    int n = tareas.Count, activas = tareas.Count(e => e.Habilitado);
                    texto = n == 0 ? "Ninguna tarea programada de otros programas"
                        : string.Format("{0} de {1} {2} {3}", activas, n,
                            n == 1 ? "tarea" : "tareas", activas == 1 ? "está activa" : "están activas");
                }
            }
            lblResumen.Text = texto;
            Acomodar();
        }

        // ------------------------------------------------------------------ cambios

        private void Tabla_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colActivo.Index || ocupado) return;
            ElementoInicio el = tabla.Rows[e.RowIndex].Tag as ElementoInicio;
            if (el != null && el.RequiereAdmin && !Util.EsAdmin)
            {
                BeginInvoke(new Action(() => AvisarAdmin(el)));
            }
        }

        private void Tabla_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (ignorarCambios || e.RowIndex < 0 || e.ColumnIndex != colActivo.Index) return;
            DataGridViewRow fila = tabla.Rows[e.RowIndex];
            ElementoInicio el = fila.Tag as ElementoInicio;
            if (el == null) return;
            object v = fila.Cells[colActivo.Index].Value;
            bool nuevo = v is bool && (bool)v;
            if (nuevo == el.Habilitado) return;
            // Se procesa fuera del evento de la tabla (puede abrir diálogos)
            BeginInvoke(new Action(() => ProcesarCambio(fila, el, nuevo)));
        }

        private async void ProcesarCambio(DataGridViewRow fila, ElementoInicio el, bool nuevo)
        {
            if (IsDisposed) return;
            try { tabla.EndEdit(); } catch { }
            if (ocupado || fila.DataGridView != tabla)
            {
                Sincronizar(fila, el);
                return;
            }

            string error = null;
            ocupado = true;
            try
            {
                if (el.RequiereAdmin && !Util.EsAdmin)
                {
                    Sincronizar(fila, el);
                    AvisarAdmin(el);
                    return;
                }
                if (!nuevo && el.MejorMantener)
                {
                    bool seguir = Dialogo.Confirmar(FindForm(), "¿Seguro que quieres apagar «" + el.Nombre + "»?",
                        "Parece un driver, un antivirus o un anticheat. Si no se abre al encender, algo puede dejar de funcionar: " +
                        "por ejemplo, Riot Vanguard hace falta para jugar a League of Legends o Valorant, y los drivers de sonido " +
                        "o del touchpad pueden perder funciones.",
                        "Apagar igualmente", true);
                    if (!seguir || IsDisposed)
                    {
                        Sincronizar(fila, el);
                        return;
                    }
                }

                tabla.Enabled = false;
                btnActualizar.Enabled = false;
                await Task.Run(() => ElementosInicio.CambiarEstado(el, nuevo));
            }
            catch (Exception ex)
            {
                Util.Log("Inicio: no se pudo cambiar '" + el.Nombre + "': " + ex.Message);
                error = ex.Message;
            }
            finally
            {
                ocupado = false;
                if (!IsDisposed)
                {
                    tabla.Enabled = true;
                    btnActualizar.Enabled = !cargando;
                    // Si llegó una lista nueva mientras tanto, se pinta ahora
                    if (pendienteRellenar)
                    {
                        pendienteRellenar = false;
                        BeginInvoke(new Action(Rellenar));
                    }
                }
            }
            if (IsDisposed) return;

            // La casilla refleja siempre el estado real (si falló, vuelve a como estaba)
            if (fila.DataGridView == tabla)
            {
                Sincronizar(fila, el);
                EstiloFila(fila, el);
            }
            ActualizarContadores();

            if (error != null)
            {
                Dialogo.Error(FindForm(), "No se pudo cambiar «" + el.Nombre + "»", error);
            }
        }

        /// <summary>Pone la casilla igual que el estado real del elemento (sin disparar cambios).</summary>
        private void Sincronizar(DataGridViewRow fila, ElementoInicio el)
        {
            if (fila.DataGridView != tabla) return;
            ignorarCambios = true;
            try
            {
                fila.Cells[colActivo.Index].Value = el.Habilitado;
                tabla.InvalidateRow(fila.Index);
            }
            catch { }
            finally
            {
                ignorarCambios = false;
            }
        }

        private void AvisarAdmin(ElementoInicio el)
        {
            if (IsDisposed) return;
            Dialogo.Info(FindForm(), "Hacen falta permisos de administrador",
                el.Tipo == TipoInicio.Tarea
                    ? "Las tareas programadas solo se pueden activar o apagar como administrador. Abre el Optimizador como administrador para cambiar esta."
                    : "Este programa se abre para todos los usuarios del PC, así que solo se puede cambiar como administrador. Abre el Optimizador como administrador para cambiarlo.");
        }

        private void AbrirAdministrador()
        {
            if (!Util.Abrir(Util.RutaSistema("taskmgr.exe"), "/0 /startup"))
            {
                Dialogo.Error(FindForm(), "No se pudo abrir", "No se pudo abrir el Administrador de tareas. Puedes abrirlo con Ctrl + Mayús + Esc.");
            }
        }
    }
}
