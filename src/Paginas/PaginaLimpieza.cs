using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Liberar espacio: lista de archivos basura con su tamaño para elegir qué borrar.</summary>
    class PaginaLimpieza : PaginaBase
    {
        private const int ColMarca = 0, ColNombre = 1, ColDetalle = 2, ColTamano = 3, ColEstado = 4;
        private const float PesoNombre = 28f, PesoDetalle = 50f, PesoTamano = 11f, PesoEstado = 13f;
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        private readonly List<Objetivo> objetivos;
        private readonly DataGridView tabla;
        private readonly Tarjeta pie;
        private readonly Label lblSeleccion, lblEstado;
        private readonly BarraUso barra;
        private readonly Boton btnLimpiar, btnCancelar, btnAnalizar, btnRecomendado;
        private readonly CancellationTokenSource ctsVida = new CancellationTokenSource();

        // Estado de cada objetivo (por Id)
        private readonly HashSet<string> sinMedir = new HashSet<string>();
        private readonly HashSet<string> limpiados = new HashSet<string>();
        private readonly HashSet<string> fallidos = new HashSet<string>();
        private readonly Dictionary<string, List<string>> bloqueos = new Dictionary<string, List<string>>();
        private string limpiandoId;

        private bool analizado, analizando, limpiando, verBarra, verCancelar;
        private CancellationTokenSource ctsLimpieza;

        public PaginaLimpieza() : base("Liberar espacio", "Borra archivos basura del disco C:")
        {
            objetivos = MotorLimpieza.ObtenerObjetivos();

            // ---- Botones del encabezado (el primero queda a la derecha) ----
            btnAnalizar = new Boton("Analizar de nuevo", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnAnalizar.Margin = new Padding(Tema.S(4), 0, 0, 0);
            btnAnalizar.Click += delegate { Analizar(false); };
            btnRecomendado = new Boton("Marcar recomendado", EstiloBoton.Fantasma, Tema.IcoCheck);
            btnRecomendado.Margin = new Padding(Tema.S(4), 0, 0, 0);
            btnRecomendado.Click += delegate { MarcarRecomendado(); };
            Acciones.Controls.Add(btnAnalizar);
            Acciones.Controls.Add(btnRecomendado);

            // ---- Tabla ----
            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ShowCellToolTips = true;

            DataGridViewCheckBoxColumn cMarca = new DataGridViewCheckBoxColumn();
            cMarca.HeaderText = "";
            cMarca.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            cMarca.Width = Tema.S(52);
            cMarca.Resizable = DataGridViewTriState.False;
            cMarca.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn cNombre = ColumnaTexto("Qué se borra", PesoNombre);
            cNombre.DefaultCellStyle.Font = Tema.FuenteSemibold(9.5f);
            cNombre.SortMode = DataGridViewColumnSortMode.Automatic;
            cNombre.MinimumWidth = Tema.S(196);

            DataGridViewTextBoxColumn cDetalle = ColumnaTexto("Detalle", PesoDetalle);
            cDetalle.MinimumWidth = Tema.S(120);
            cDetalle.DefaultCellStyle.ForeColor = Tema.TextoSuave;
            cDetalle.DefaultCellStyle.SelectionForeColor = Tema.TextoSuave;
            cDetalle.DefaultCellStyle.Font = Tema.Fuente(9f);

            DataGridViewTextBoxColumn cTamano = ColumnaTexto("Tamaño", PesoTamano);
            cTamano.MinimumWidth = Tema.S(100);
            cTamano.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            cTamano.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            cTamano.SortMode = DataGridViewColumnSortMode.Automatic;

            DataGridViewTextBoxColumn cEstado = ColumnaTexto("Estado", PesoEstado);
            cEstado.MinimumWidth = Tema.S(124);
            cEstado.DefaultCellStyle.Font = Tema.Fuente(9f);

            tabla.Columns.AddRange(new DataGridViewColumn[] { cMarca, cNombre, cDetalle, cTamano, cEstado });
            // El DataGridView reescribe FillWeight cuando una columna toca su ancho mínimo
            // (p. ej. mientras la tabla aún es pequeña): se restauran los pesos en cada cambio de tamaño.
            tabla.SizeChanged += delegate { RestaurarPesos(); };

            foreach (Objetivo o in objetivos)
            {
                int i = tabla.Rows.Add();
                DataGridViewRow r = tabla.Rows[i];
                r.Tag = o;
                r.Cells[ColMarca].Value = o.Recomendado && o.Disponible;
                r.Cells[ColNombre].Value = o.Nombre;
                r.Cells[ColDetalle].Value = o.Descripcion;
                r.Cells[ColDetalle].ToolTipText = o.Descripcion;
                if (!o.Disponible)
                {
                    r.Cells[ColMarca].ReadOnly = true;
                    r.DefaultCellStyle.ForeColor = Tema.TextoTenue;
                    r.DefaultCellStyle.SelectionForeColor = Tema.TextoTenue;
                }
                if (o.Disponible) sinMedir.Add(o.Id);
                ActualizarFila(r);
            }

            tabla.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == ColMarca) ActualizarTotal();
            };
            tabla.CellBeginEdit += delegate(object s, DataGridViewCellCancelEventArgs e)
            {
                // Mientras se limpia no se puede cambiar la selección
                if (limpiando) e.Cancel = true;
            };
            tabla.SortCompare += Tabla_SortCompare;

            Tarjeta marco = new Tarjeta();
            marco.Dock = DockStyle.Fill;
            marco.Margin = new Padding(0);
            marco.Padding = new Padding(Tema.S(12), Tema.S(6), Tema.S(12), Tema.S(6));
            marco.Controls.Add(tabla);

            // ---- Barra inferior ----
            pie = new Tarjeta();
            pie.Dock = DockStyle.Fill;
            pie.Margin = new Padding(0);
            pie.Padding = new Padding(Tema.S(20), Tema.S(12), Tema.S(20), Tema.S(12));

            lblSeleccion = Tema.Etiqueta("Seleccionado: …", 12f, Tema.Texto, true);
            lblEstado = new Label();
            lblEstado.AutoSize = false;
            lblEstado.AutoEllipsis = true;
            lblEstado.UseMnemonic = false;
            lblEstado.Font = Tema.Fuente(9f);
            lblEstado.ForeColor = Tema.TextoSuave;
            lblEstado.Margin = new Padding(0);
            lblEstado.Height = lblEstado.PreferredHeight;

            barra = new BarraUso();
            barra.Height = Tema.S(6);
            barra.Visible = false;

            btnLimpiar = new Boton("Limpiar ahora", EstiloBoton.Primario, Tema.IcoLimpieza);
            btnLimpiar.Margin = new Padding(0);
            btnLimpiar.Click += BtnLimpiar_Click;

            btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario, Tema.IcoCancelar);
            btnCancelar.Margin = new Padding(0);
            btnCancelar.Visible = false;
            btnCancelar.Click += delegate
            {
                if (ctsLimpieza == null) return;
                try { ctsLimpieza.Cancel(); } catch { }
                btnCancelar.Enabled = false;
                lblEstado.Text = "Cancelando… se para en cuanto termine lo que está en marcha.";
            };

            pie.Controls.AddRange(new Control[] { lblSeleccion, lblEstado, barra, btnCancelar, btnLimpiar });
            pie.Layout += delegate { ColocarPie(); };

            TableLayoutPanel grilla = new TableLayoutPanel();
            grilla.Dock = DockStyle.Fill;
            grilla.Margin = new Padding(0);
            grilla.Padding = new Padding(0);
            grilla.ColumnCount = 1;
            grilla.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grilla.RowCount = 3;
            grilla.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grilla.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(16)));
            grilla.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(80)));
            grilla.Controls.Add(marco, 0, 0);
            grilla.Controls.Add(pie, 0, 2);
            Contenido.Controls.Add(grilla);

            ActualizarTotal();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { ctsVida.Cancel(); } catch { }
                try { if (ctsLimpieza != null) ctsLimpieza.Cancel(); } catch { }
            }
            base.Dispose(disposing);
        }

        public override void AlMostrar()
        {
            // Solo se analiza sola la primera vez (es de solo lectura); luego, con el botón o tras limpiar
            if (!analizado && !analizando && !limpiando)
            {
                Analizar(false);
                try
                {
                    if (tabla.IsHandleCreated)
                    {
                        tabla.BeginInvoke((MethodInvoker)delegate { tabla.ClearSelection(); });
                    }
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------ análisis (solo lectura)

        private async void Analizar(bool trasLimpieza)
        {
            if (analizando || limpiando) return;
            analizando = true;
            analizado = true;
            if (!trasLimpieza)
            {
                limpiados.Clear();
                fallidos.Clear();
            }
            CancellationToken ct = ctsVida.Token;
            sinMedir.Clear();
            foreach (Objetivo o in objetivos)
            {
                if (o.Disponible) sinMedir.Add(o.Id);
            }
            foreach (DataGridViewRow r in tabla.Rows) ActualizarFila(r);
            barra.Valor = 0;
            MostrarBarra(true);
            AplicarEstado();
            ActualizarTotal();

            try
            {
                for (int i = 0; i < objetivos.Count; i++)
                {
                    Objetivo o = objetivos[i];
                    lblEstado.Text = "Analizando " + Minuscula(o.Nombre) + "…";
                    List<string> abiertos = await Task.Run(() =>
                    {
                        if (o.Disponible)
                        {
                            try { MotorLimpieza.Medir(o, ct); }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                o.Tamano = -1;
                                Util.Log("Limpieza: no se pudo medir '" + o.Nombre + "': " + ex.Message);
                            }
                        }
                        return MotorLimpieza.ProgramasAbiertosQueBloquean(o);
                    });
                    if (IsDisposed) return;
                    sinMedir.Remove(o.Id);
                    bloqueos[o.Id] = abiertos;
                    ActualizarFila(FilaDe(o));
                    ActualizarTotal();
                    barra.Valor = (i + 1) * 100.0 / objetivos.Count;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Util.Log("Limpieza: error al analizar: " + ex);
            }
            finally
            {
                analizando = false;
            }
            if (IsDisposed) return;

            sinMedir.Clear();
            foreach (DataGridViewRow r in tabla.Rows) ActualizarFila(r);
            MostrarBarra(false);
            AplicarEstado();
            ActualizarTotal();
        }

        // ------------------------------------------------------------------ limpieza

        private async void BtnLimpiar_Click(object sender, EventArgs e)
        {
            if (limpiando || analizando) return;
            Form dueno = FindForm();
            try
            {
                tabla.EndEdit();
                List<Objetivo> elegidos = new List<Objetivo>();
                foreach (DataGridViewRow r in tabla.Rows)
                {
                    Objetivo o = (Objetivo)r.Tag;
                    if (Marcado(r) && o.Disponible) elegidos.Add(o);
                }
                // Mismo orden que la lista original, aunque la tabla esté ordenada
                elegidos.Sort(delegate(Objetivo a, Objetivo b) { return objetivos.IndexOf(a).CompareTo(objetivos.IndexOf(b)); });

                if (elegidos.Count == 0)
                {
                    Dialogo.Info(dueno, "No hay nada marcado", "Marca en la lista al menos una cosa que quieras borrar.");
                    return;
                }

                Objetivo pap = elegidos.Find(delegate(Objetivo o) { return o.Especial == TipoEspecial.Papelera; });
                if (pap != null)
                {
                    string tam = pap.Tamano > 0 ? " (" + Util.FormatoTamano(pap.Tamano) + ")" : "";
                    if (!Dialogo.Confirmar(dueno, "¿Vaciar la papelera?",
                        "Lo que hay en la papelera" + tam + " se borrará para siempre y no podrás recuperarlo.\n\n" +
                        "Si no estás seguro, quita la marca de «Papelera de reciclaje» y revísala antes.",
                        "Sí, vaciarla", true)) return;
                }

                // Se vuelve a mirar justo ahora qué programas están abiertos
                limpiando = true;
                btnLimpiar.Enabled = false;
                List<string> abiertos;
                try
                {
                    abiertos = await Task.Run(() =>
                    {
                        List<string> l = new List<string>();
                        foreach (Objetivo o in elegidos)
                        {
                            foreach (string p in MotorLimpieza.ProgramasAbiertosQueBloquean(o))
                            {
                                string n = NombreAmigable(p);
                                if (!l.Contains(n)) l.Add(n);
                            }
                        }
                        return l;
                    });
                }
                finally
                {
                    limpiando = false;
                    if (!IsDisposed) btnLimpiar.Enabled = true;
                }
                if (IsDisposed) return;

                if (abiertos.Count > 0)
                {
                    string quien = UnirConY(abiertos) + (abiertos.Count == 1 ? " está abierto" : " están abiertos");
                    if (!Dialogo.Confirmar(dueno, "Hay programas abiertos",
                        quien + ". Los archivos que estén usando se saltarán.\n\nSi quieres limpiar más, ciérralos antes de continuar.",
                        "Continuar", false)) return;
                }

                long estimado = 0;
                List<string> nombres = new List<string>();
                foreach (Objetivo o in elegidos)
                {
                    if (o.Tamano > 0) estimado += o.Tamano;
                    nombres.Add(o.Nombre);
                }
                string cuanto = estimado > 0 ? "Se borrarán unos " + Util.FormatoTamano(estimado) + " de archivos basura:" : "Se borrarán los archivos basura de:";
                string nota = pap != null
                    ? "Tus documentos, fotos y descargas no se tocan (salvo lo que ya está en la papelera)."
                    : "Tus documentos, fotos y descargas no se tocan.";
                if (!Dialogo.Confirmar(dueno, "¿Limpiar ahora?",
                    cuanto + "\n\n•  " + string.Join("\n•  ", nombres) + "\n\n" + nota, "Limpiar", false)) return;

                await Limpiar(elegidos, abiertos.Count > 0, dueno);
            }
            catch (Exception ex)
            {
                Util.Log("Limpieza: error inesperado: " + ex);
                limpiando = false;
                limpiandoId = null;
                if (!IsDisposed)
                {
                    MostrarBarra(false);
                    AplicarEstado();
                    ActualizarTotal();
                    Dialogo.Error(dueno, "Algo salió mal", "No se pudo completar la limpieza.\n\nEl detalle se guardó en el registro de la app.");
                }
            }
        }

        private async Task Limpiar(List<Objetivo> elegidos, bool habiaAbiertos, Form dueno)
        {
            limpiando = true;
            ctsLimpieza = new CancellationTokenSource();
            CancellationToken ct = ctsLimpieza.Token;
            if (dueno != null) dueno.FormClosing += EvitarCierre;
            fallidos.Clear();
            barra.Valor = 0;
            MostrarBarra(true);
            AplicarEstado();

            long libreAntes = Sistema.Disco().Libre;
            long liberado = 0;
            bool cancelado = false;
            List<string> noSePudo = new List<string>();
            try
            {
                for (int i = 0; i < elegidos.Count; i++)
                {
                    Objetivo o = elegidos[i];
                    if (ct.IsCancellationRequested) { cancelado = true; break; }
                    barra.Valor = i * 100.0 / elegidos.Count;
                    lblEstado.Text = "Limpiando " + Minuscula(o.Nombre) + "…  (" + (i + 1) + " de " + elegidos.Count + ")";
                    DataGridViewRow fila = FilaDe(o);
                    limpiandoId = o.Id;
                    ActualizarFila(fila);

                    // Comprobación final justo antes de borrar
                    if (!o.Disponible)
                    {
                        noSePudo.Add(o.Nombre);
                        fallidos.Add(o.Id);
                        limpiandoId = null;
                        ActualizarFila(fila);
                        continue;
                    }
                    try
                    {
                        long l = await Task.Run(() => MotorLimpieza.Limpiar(o, ct, null));
                        liberado += l;
                        limpiados.Add(o.Id);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelado = true;
                    }
                    catch (Exception ex)
                    {
                        Util.Log("Limpieza: error limpiando '" + o.Nombre + "': " + ex);
                        noSePudo.Add(o.Nombre);
                        fallidos.Add(o.Id);
                    }
                    limpiandoId = null;
                    if (IsDisposed) return;
                    ActualizarFila(fila);
                    if (cancelado) break;
                }
                barra.Valor = 100;
            }
            finally
            {
                limpiando = false;
                limpiandoId = null;
                ctsLimpieza = null;
                if (dueno != null) dueno.FormClosing -= EvitarCierre;
            }
            if (IsDisposed) return;

            long libreDespues = Sistema.Disco().Libre;
            Util.Log("Limpieza manual terminada: " + Util.FormatoTamano(liberado) + (cancelado ? " (cancelada)" : ""));
            MostrarBarra(false);
            AplicarEstado();
            ActualizarTotal();

            string msg = cancelado
                ? "Has parado la limpieza. Antes de parar se liberaron " + Util.FormatoTamano(liberado) + "."
                : (liberado > 0 ? "Has liberado " + Util.FormatoTamano(liberado) + "." : "No había casi nada que borrar.");
            long ganado = libreDespues - libreAntes;
            if (ganado >= 1024L * 1024)
            {
                msg += "\nEl espacio libre en " + Sistema.LetraSistema + " ha aumentado " + Util.FormatoTamano(ganado) + ".";
            }
            if (habiaAbiertos) msg += "\n\nLos archivos que estaban en uso por programas abiertos se han saltado.";
            if (noSePudo.Count > 0)
            {
                msg += "\n\nNo se pudo limpiar: " + UnirConY(noSePudo) + ". El detalle está en el registro de la app.";
            }
            Dialogo.Exito(dueno, cancelado ? "Limpieza cancelada" : "¡Listo!", msg);

            if (!IsDisposed) Analizar(true);
        }

        private void EvitarCierre(object sender, FormClosingEventArgs e)
        {
            if (limpiando && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Dialogo.Info(FindForm(), "Espera un momento",
                    "Se está limpiando. Pulsa «Cancelar» o espera a que termine para cerrar la app.");
            }
        }

        // ------------------------------------------------------------------ tabla

        private static DataGridViewTextBoxColumn ColumnaTexto(string titulo, float peso)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }

        private void RestaurarPesos()
        {
            tabla.Columns[ColNombre].FillWeight = PesoNombre;
            tabla.Columns[ColDetalle].FillWeight = PesoDetalle;
            tabla.Columns[ColTamano].FillWeight = PesoTamano;
            tabla.Columns[ColEstado].FillWeight = PesoEstado;
        }

        private void Tabla_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Index != ColTamano) return;
            Objetivo a = tabla.Rows[e.RowIndex1].Tag as Objetivo;
            Objetivo b = tabla.Rows[e.RowIndex2].Tag as Objetivo;
            long ta = a == null || !a.Disponible ? -2 : a.Tamano;
            long tb = b == null || !b.Disponible ? -2 : b.Tamano;
            e.SortResult = ta.CompareTo(tb);
            e.Handled = true;
        }

        private DataGridViewRow FilaDe(Objetivo o)
        {
            foreach (DataGridViewRow r in tabla.Rows)
            {
                if (r.Tag == o) return r;
            }
            return null;
        }

        private static bool Marcado(DataGridViewRow r)
        {
            object v = r.Cells[ColMarca].Value;
            return v is bool && (bool)v;
        }

        private void ActualizarFila(DataGridViewRow r)
        {
            if (r == null) return;
            Objetivo o = (Objetivo)r.Tag;

            string tam;
            Color colorTam = Tema.Texto;
            if (!o.Disponible) { tam = "—"; colorTam = Tema.TextoTenue; }
            else if (sinMedir.Contains(o.Id)) { tam = "Calculando…"; colorTam = Tema.TextoTenue; }
            else if (o.Tamano < 0) { tam = "—"; colorTam = Tema.TextoTenue; }
            else if (o.Tamano == 0) { tam = "Vacío"; colorTam = Tema.TextoTenue; }
            else tam = Util.FormatoTamano(o.Tamano);
            Poner(r.Cells[ColTamano], tam, colorTam, null);

            string estado = "", ayuda = null;
            Color colorEstado = Tema.TextoSuave;
            List<string> abiertos;
            if (!o.Disponible)
            {
                estado = "Necesita admin";
                colorEstado = Tema.Aviso;
                ayuda = "Abre la app como administrador para poder limpiar esto.";
            }
            else if (o.Id == limpiandoId)
            {
                estado = "Limpiando…";
                colorEstado = Tema.Acento;
            }
            else if (fallidos.Contains(o.Id))
            {
                estado = "No se pudo";
                colorEstado = Tema.Peligro;
                ayuda = "Algo falló al limpiar esto. El detalle está en el registro de la app.";
            }
            else if (bloqueos.TryGetValue(o.Id, out abiertos) && abiertos.Count > 0)
            {
                List<string> amigables = new List<string>();
                foreach (string p in abiertos)
                {
                    string n = NombreAmigable(p);
                    if (!amigables.Contains(n)) amigables.Add(n);
                }
                estado = "Cierra " + amigables[0] + (amigables.Count > 1 ? "…" : "");
                colorEstado = Tema.Aviso;
                ayuda = "Cierra " + UnirConY(amigables) + " para poder limpiarlo todo. Si no, se saltarán los archivos que esté usando.";
            }
            else if (limpiados.Contains(o.Id))
            {
                estado = "Limpio";
                colorEstado = Tema.Exito;
            }
            Poner(r.Cells[ColEstado], estado, colorEstado, ayuda);
        }

        private static void Poner(DataGridViewCell c, string texto, Color color, string ayuda)
        {
            c.Value = texto;
            c.Style.ForeColor = color;
            c.Style.SelectionForeColor = color;
            c.ToolTipText = ayuda ?? "";
        }

        private void ActualizarTotal()
        {
            long total = 0;
            int marcados = 0;
            bool pendiente = false;
            foreach (DataGridViewRow r in tabla.Rows)
            {
                Objetivo o = (Objetivo)r.Tag;
                if (!o.Disponible || !Marcado(r)) continue;
                marcados++;
                if (sinMedir.Contains(o.Id)) pendiente = true;
                else if (o.Tamano > 0) total += o.Tamano;
            }

            if (marcados == 0) lblSeleccion.Text = "Nada seleccionado";
            else if (total == 0 && !pendiente) lblSeleccion.Text = "Seleccionado: 0 MB";
            else lblSeleccion.Text = "Seleccionado: " + Util.FormatoTamano(total) + (pendiente ? "…" : "");

            if (!analizando && !limpiando)
            {
                string s;
                if (marcados == 0) s = "Marca en la lista lo que quieras borrar.";
                else if (marcados == 1) s = "1 elemento marcado";
                else s = marcados + " elementos marcados";
                if (!Util.EsAdmin) s += "  ·  Sin permisos de administrador algunas cosas no se pueden limpiar";
                lblEstado.Text = s;
            }
        }

        private void MarcarRecomendado()
        {
            if (limpiando) return;
            tabla.EndEdit();
            foreach (DataGridViewRow r in tabla.Rows)
            {
                Objetivo o = (Objetivo)r.Tag;
                if (r.Cells[ColMarca].ReadOnly || !o.Disponible) continue;
                r.Cells[ColMarca].Value = o.Recomendado;
            }
            ActualizarTotal();
        }

        // Activa/desactiva botones y casillas según lo que esté pasando
        private void AplicarEstado()
        {
            bool ocupado = analizando || limpiando;
            btnAnalizar.Enabled = !ocupado;
            btnRecomendado.Enabled = !limpiando;
            btnLimpiar.Enabled = !ocupado;
            btnLimpiar.Text = limpiando ? "Limpiando…" : "Limpiar ahora";
            verCancelar = limpiando && ctsLimpieza != null && !ctsLimpieza.IsCancellationRequested;
            btnCancelar.Visible = verCancelar;
            btnCancelar.Enabled = verCancelar;
            foreach (DataGridViewRow r in tabla.Rows)
            {
                Objetivo o = (Objetivo)r.Tag;
                r.Cells[ColMarca].ReadOnly = limpiando || !o.Disponible;
            }
            tabla.InvalidateColumn(ColMarca);
            pie.PerformLayout();
        }

        private void MostrarBarra(bool ver)
        {
            verBarra = ver;
            barra.Visible = ver;
            pie.PerformLayout();
        }

        private void ColocarPie()
        {
            int p = pie.Padding.Left, w = pie.ClientSize.Width, h = pie.ClientSize.Height;
            int x = w - pie.Padding.Right - btnLimpiar.Width;
            btnLimpiar.Location = new Point(x, (h - btnLimpiar.Height) / 2);
            if (verCancelar)
            {
                x -= Tema.S(8) + btnCancelar.Width;
                btnCancelar.Location = new Point(x, (h - btnCancelar.Height) / 2);
            }
            if (verBarra)
            {
                int ancho = Math.Max(Tema.S(80), Math.Min(Tema.S(220), (w - 2 * p) / 4));
                x -= Tema.S(24) + ancho;
                barra.SetBounds(x, (h - barra.Height) / 2, ancho, barra.Height);
            }
            int alto = lblSeleccion.Height + Tema.S(4) + lblEstado.Height;
            int y = (h - alto) / 2;
            lblSeleccion.Location = new Point(p, y);
            lblEstado.SetBounds(p, y + lblSeleccion.Height + Tema.S(4), Math.Max(Tema.S(40), x - Tema.S(24) - p), lblEstado.Height);
        }

        // ------------------------------------------------------------------ texto

        private static string NombreAmigable(string proceso)
        {
            switch ((proceso ?? "").ToLowerInvariant())
            {
                case "chrome": return "Chrome";
                case "msedge": return "Edge";
                case "brave": return "Brave";
                case "opera": return "Opera";
                case "firefox": return "Firefox";
                case "discord": return "Discord";
            }
            return proceso;
        }

        private static string UnirConY(List<string> l)
        {
            if (l.Count == 0) return "";
            if (l.Count == 1) return l[0];
            return string.Join(", ", l.GetRange(0, l.Count - 1)) + " y " + l[l.Count - 1];
        }

        private static string Minuscula(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return char.ToLower(s[0], Es) + s.Substring(1);
        }
    }
}
