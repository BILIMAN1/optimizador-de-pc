using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Busca los archivos más grandes de las carpetas personales y deja mandarlos a la papelera.</summary>
    class PaginaArchivos : PaginaBase
    {
        private const long MB = 1024L * 1024;
        private const int MaxResultados = 200;

        private enum FinBusqueda { Ninguna, Completa, Cancelada, Error }

        private class ArchivoGrande
        {
            public string Nombre, Ruta, Carpeta;
            public long Tamano;
            public DateTime Modificado;
        }

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        private readonly string perfil;
        private readonly List<TarjetaCarpeta> carpetas = new List<TarjetaCarpeta>();
        private readonly long[] umbrales = { 100 * MB, 500 * MB, 1024 * MB };
        private readonly Boton[] segmentos = new Boton[3];
        private readonly ToolTip ayuda = new ToolTip();
        private long umbral = 500 * MB;

        private Panel filaCarpetas, cabecera;
        private Label lblResumen, lblDetalle;
        private FlowLayoutPanel accionesTabla, progresoTabla;
        private BarraUso barra;
        private Boton btnUbicacion, btnPapelera, btnCancelar, btnBuscar;
        private DataGridView tabla;
        private DataGridViewTextBoxColumn colNombre, colCarpeta, colTamano, colFecha;
        private EstadoVacio vacio;

        private List<ArchivoGrande> resultados = new List<ArchivoGrande>();
        private CancellationTokenSource cts, ctsCarpetas;
        private int generacion;
        private long umbralEscaneado = -1, umbralEnCurso;
        private bool iniciado, escaneando, borrando, recortado;
        private FinBusqueda fin = FinBusqueda.Ninguna;
        private string aviso;
        private Color colorAviso = Tema.TextoTenue;

        public PaginaArchivos() : base("Archivos grandes", "Encuentra lo que más ocupa en tus carpetas")
        {
            perfil = (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "").TrimEnd('\\');

            TableLayoutPanel raiz = new TableLayoutPanel();
            raiz.Dock = DockStyle.Fill;
            raiz.Margin = new Padding(0);
            raiz.Padding = new Padding(0);
            raiz.ColumnCount = 1;
            raiz.RowCount = 3;
            raiz.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            CrearFilaCarpetas();
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, carpetas.Count > 0 ? Tema.S(68 + 16) : 0));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(36 + 16)));
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            raiz.Controls.Add(filaCarpetas, 0, 0);
            raiz.Controls.Add(CrearBarraUmbral(), 0, 1);
            raiz.Controls.Add(CrearTarjetaTabla(), 0, 2);
            Contenido.Controls.Add(raiz);

            btnBuscar = new Boton("Buscar de nuevo", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnBuscar.Margin = new Padding(0);
            btnBuscar.Click += delegate(object s, EventArgs e) { CalcularCarpetas(); Escanear(); };
            Acciones.Controls.Add(btnBuscar);

            ActualizarVista();
        }

        // ------------------------------------------------------------------ construcción

        private void CrearFilaCarpetas()
        {
            string[,] defs =
            {
                { "Descargas", Sistema.CarpetaDescargas },
                { "Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                { "Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
                { "Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
                { "Imágenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
                { "Música", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) }
            };
            HashSet<string> vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < defs.GetLength(0); i++)
            {
                string ruta = defs[i, 1];
                if (string.IsNullOrEmpty(ruta)) continue;
                ruta = ruta.TrimEnd('\\');
                bool existe;
                try { existe = Directory.Exists(ruta); } catch { existe = false; }
                if (!existe || !vistas.Add(ruta)) continue;
                carpetas.Add(new TarjetaCarpeta(defs[i, 0], ruta));
            }

            filaCarpetas = new Panel();
            filaCarpetas.Dock = DockStyle.Fill;
            filaCarpetas.Margin = new Padding(0, 0, 0, Tema.S(16));
            foreach (TarjetaCarpeta c in carpetas)
            {
                c.Click += delegate(object s, EventArgs e)
                {
                    TarjetaCarpeta t = (TarjetaCarpeta)s;
                    if (!Util.Abrir(t.Ruta, null))
                    {
                        Dialogo.Error(FindForm(), "No se pudo abrir la carpeta", "Puede que la hayan movido o borrado.");
                    }
                };
                ayuda.SetToolTip(c, "Abrir " + c.Ruta);
                filaCarpetas.Controls.Add(c);
            }
            filaCarpetas.Resize += delegate(object s, EventArgs e) { ColocarCarpetas(); };
        }

        // Todas las tarjetas con el mismo ancho y el mismo hueco entre ellas
        private void ColocarCarpetas()
        {
            int n = carpetas.Count;
            if (n == 0) return;
            int hueco = Tema.S(12);
            int ancho = filaCarpetas.ClientSize.Width;
            int alto = filaCarpetas.ClientSize.Height;
            int cada = (ancho - hueco * (n - 1)) / n;
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = i == n - 1 ? ancho - x : cada;
                carpetas[i].Bounds = new Rectangle(x, 0, Math.Max(0, w), alto);
                x += w + hueco;
            }
        }

        private Control CrearBarraUmbral()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = false;
            f.Margin = new Padding(0, 0, 0, Tema.S(16));
            f.Padding = new Padding(0);

            Label l = Tema.Etiqueta("Mostrar:", 9.5f, Tema.TextoSuave);
            l.Margin = new Padding(0, Tema.S(9), Tema.S(12), 0);
            f.Controls.Add(l);

            string[] textos = { "Más de 100 MB", "Más de 500 MB", "Más de 1 GB" };
            for (int i = 0; i < textos.Length; i++)
            {
                long u = umbrales[i];
                Boton b = new Boton(textos[i], EstiloBoton.Secundario);
                b.Margin = new Padding(0, 0, Tema.S(8), 0);
                b.Click += delegate(object s, EventArgs e) { CambiarUmbral(u); };
                segmentos[i] = b;
                f.Controls.Add(b);
            }
            PintarSegmentos();
            return f;
        }

        private Control CrearTarjetaTabla()
        {
            Tarjeta t = new Tarjeta();
            t.Dock = DockStyle.Fill;
            t.Margin = new Padding(0);
            t.Padding = new Padding(Tema.S(20), Tema.S(18), Tema.S(20), Tema.S(12));

            // Cabecera: resumen a la izquierda y botones (o progreso) a la derecha
            cabecera = new Panel();
            cabecera.Dock = DockStyle.Top;
            cabecera.Height = Tema.S(60);

            lblResumen = Tema.Etiqueta("", 11.5f, Tema.Texto, true);
            lblResumen.AutoSize = false;
            lblResumen.AutoEllipsis = true;
            lblDetalle = Tema.Etiqueta("", 9f, Tema.TextoTenue);
            lblDetalle.AutoSize = false;
            lblDetalle.AutoEllipsis = true;

            accionesTabla = FilaBotones();
            btnUbicacion = new Boton("Abrir ubicación", EstiloBoton.Fantasma, Tema.IcoArchivos);
            btnUbicacion.Margin = new Padding(0, 0, Tema.S(8), 0);
            btnUbicacion.Click += delegate(object s, EventArgs e) { AbrirUbicacion(); };
            btnPapelera = new Boton("Enviar a la papelera", EstiloBoton.Peligro);
            btnPapelera.Margin = new Padding(0);
            btnPapelera.Click += delegate(object s, EventArgs e) { EnviarAPapelera(); };
            accionesTabla.Controls.Add(btnUbicacion);
            accionesTabla.Controls.Add(btnPapelera);

            progresoTabla = FilaBotones();
            barra = new BarraUso();
            barra.Width = Tema.S(160);
            barra.Height = Tema.S(6);
            barra.Margin = new Padding(0, (Tema.S(36) - Tema.S(6)) / 2, Tema.S(16), 0);
            btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario, Tema.IcoCancelar);
            btnCancelar.Margin = new Padding(0);
            btnCancelar.Click += delegate(object s, EventArgs e) { if (cts != null) cts.Cancel(); };
            progresoTabla.Controls.Add(barra);
            progresoTabla.Controls.Add(btnCancelar);

            cabecera.Controls.Add(lblResumen);
            cabecera.Controls.Add(lblDetalle);
            cabecera.Controls.Add(accionesTabla);
            cabecera.Controls.Add(progresoTabla);
            cabecera.Resize += delegate(object s, EventArgs e) { ColocarCabecera(); };

            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = true;
            tabla.ShowCellToolTips = true;
            colNombre = Columna("Archivo", 34f, 140);
            colCarpeta = Columna("Carpeta", 38f, 120);
            colCarpeta.DefaultCellStyle.ForeColor = Tema.TextoTenue;
            colCarpeta.DefaultCellStyle.SelectionForeColor = Tema.TextoSuave;
            colTamano = Columna("Tamaño", 13f, 96);
            colTamano.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colTamano.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            colFecha = Columna("Modificado", 15f, 110);
            tabla.Columns.AddRange(new DataGridViewColumn[] { colNombre, colCarpeta, colTamano, colFecha });
            tabla.SortCompare += TablaSortCompare;
            tabla.SelectionChanged += delegate(object s, EventArgs e) { ActualizarBotones(); };
            tabla.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) AbrirUbicacion();
            };

            vacio = new EstadoVacio();
            vacio.Dock = DockStyle.Fill;

            t.Controls.Add(cabecera);
            t.Controls.Add(tabla);
            t.Controls.Add(vacio);
            tabla.BringToFront();
            vacio.BringToFront();
            return t;
        }

        private static FlowLayoutPanel FilaBotones()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = false;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Margin = new Padding(0);
            f.Padding = new Padding(0);
            return f;
        }

        private static DataGridViewTextBoxColumn Columna(string titulo, float peso, int minimo)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.MinimumWidth = Tema.S(minimo);
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.Automatic;
            return c;
        }

        private void ColocarCabecera()
        {
            if (cabecera == null) return;
            Control derecha = escaneando ? (Control)progresoTabla : accionesTabla;
            Size pd = derecha.GetPreferredSize(Size.Empty);
            derecha.Size = pd;
            derecha.Location = new Point(Math.Max(0, cabecera.Width - pd.Width), Tema.S(2));
            int anchoTexto = Math.Max(Tema.S(40), derecha.Left - Tema.S(16));
            int h1 = TextRenderer.MeasureText("Ág", lblResumen.Font).Height;
            int h2 = TextRenderer.MeasureText("Ág", lblDetalle.Font).Height;
            lblResumen.Bounds = new Rectangle(0, 0, anchoTexto, h1);
            lblDetalle.Bounds = new Rectangle(0, h1 + Tema.S(2), anchoTexto, h2);
        }

        // ------------------------------------------------------------------ estado de la vista

        public override void AlMostrar()
        {
            // Solo la primera vez: medir carpetas y buscar (todo de solo lectura)
            if (iniciado) return;
            iniciado = true;
            CalcularCarpetas();
            Escanear();
        }

        private static string TextoUmbral(long u)
        {
            return u >= 1024 * MB ? (u / (1024 * MB)) + " GB" : (u / MB) + " MB";
        }

        private static string Contar(int n)
        {
            return n == 1 ? "1 archivo" : n.ToString("N0", Es) + " archivos";
        }

        private void PintarSegmentos()
        {
            for (int i = 0; i < segmentos.Length; i++)
            {
                if (segmentos[i] != null) segmentos[i].Estilo = umbrales[i] == umbral ? EstiloBoton.Primario : EstiloBoton.Secundario;
            }
        }

        private void ActualizarVista()
        {
            if (IsDisposed) return;
            string tu = TextoUmbral(umbral);
            int n = tabla.Rows.Count;
            bool mostrarTabla = n > 0 && !escaneando;

            accionesTabla.Visible = !escaneando;
            progresoTabla.Visible = escaneando;
            if (barra.Indeterminado != escaneando) barra.Indeterminado = escaneando;
            btnBuscar.Enabled = !escaneando;

            if (escaneando)
            {
                lblResumen.Text = "Buscando archivos de más de " + tu + "…";
                lblDetalle.ForeColor = Tema.TextoTenue;
                vacio.Establecer(Tema.IcoBuscar, Tema.Acento, "Un momento",
                    "Estamos revisando tus carpetas personales. La primera vez puede tardar un par de minutos.");
            }
            else if (n > 0)
            {
                long total = 0;
                foreach (DataGridViewRow r in tabla.Rows)
                {
                    ArchivoGrande a = r.Tag as ArchivoGrande;
                    if (a != null) total += a.Tamano;
                }
                lblResumen.Text = Contar(n) + "  ·  " + Util.FormatoTamano(total);
                if (aviso != null)
                {
                    lblDetalle.Text = aviso;
                    lblDetalle.ForeColor = colorAviso;
                }
                else if (fin == FinBusqueda.Cancelada || fin == FinBusqueda.Error)
                {
                    lblDetalle.Text = "La búsqueda no terminó: puede que falten archivos";
                    lblDetalle.ForeColor = Tema.Aviso;
                }
                else
                {
                    lblDetalle.Text = (recortado && n >= MaxResultados ? "Los " + MaxResultados + " más grandes" : "De más de " + tu) +
                                      " en tus carpetas personales  ·  Doble clic para ver dónde está";
                    lblDetalle.ForeColor = Tema.TextoTenue;
                }
            }
            else
            {
                lblDetalle.Text = aviso ?? "";
                lblDetalle.ForeColor = colorAviso;
                if (fin == FinBusqueda.Error)
                {
                    lblResumen.Text = "Algo salió mal";
                    vacio.Establecer(Tema.IcoAviso, Tema.Peligro, "No se pudo terminar la búsqueda",
                        "Pulsa “Buscar de nuevo” para intentarlo otra vez.");
                }
                else if (fin == FinBusqueda.Cancelada)
                {
                    lblResumen.Text = "Búsqueda cancelada";
                    vacio.Establecer(Tema.IcoCancelar, Tema.TextoTenue, "No se ha revisado todo",
                        "Pulsa “Buscar de nuevo” cuando quieras.");
                }
                else if (fin == FinBusqueda.Completa)
                {
                    lblResumen.Text = "Ningún archivo de más de " + tu;
                    vacio.Establecer(Tema.IcoCheck, Tema.Exito, "Todo en orden",
                        "No tienes archivos de más de " + tu + " en tus carpetas personales." +
                        (umbral > umbrales[0] ? " Prueba con un tamaño menor." : ""));
                }
                else
                {
                    lblResumen.Text = "Archivos grandes";
                    vacio.Establecer(Tema.IcoBuscar, Tema.TextoTenue, "Todavía no has buscado",
                        "Pulsa “Buscar de nuevo” para revisar tus carpetas.");
                }
            }

            tabla.Visible = mostrarTabla;
            vacio.Visible = !mostrarTabla;
            ColocarCabecera();
            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            if (IsDisposed || btnPapelera == null) return;
            bool hay = !escaneando && tabla.Rows.Count > 0 && tabla.SelectedRows.Count == 1;
            btnUbicacion.Enabled = hay;
            btnPapelera.Enabled = hay && !borrando;
        }

        private ArchivoGrande Seleccionado()
        {
            if (tabla.SelectedRows.Count != 1) return null;
            return tabla.SelectedRows[0].Tag as ArchivoGrande;
        }

        // ------------------------------------------------------------------ umbral

        private void CambiarUmbral(long nuevo)
        {
            if (nuevo == umbral) return;
            umbral = nuevo;
            aviso = null;
            PintarSegmentos();
            if (escaneando)
            {
                // Si ya se busca con un tamaño menor, basta con filtrar al terminar
                if (nuevo < umbralEnCurso) Escanear();
                else ActualizarVista();
                return;
            }
            if (fin == FinBusqueda.Completa && umbralEscaneado >= 0 && nuevo >= umbralEscaneado)
            {
                MostrarResultados();
                ActualizarVista();
            }
            else
            {
                Escanear();
            }
        }

        // ------------------------------------------------------------------ tamaño de las carpetas

        private async void CalcularCarpetas()
        {
            if (ctsCarpetas != null) ctsCarpetas.Cancel();
            CancellationTokenSource c = new CancellationTokenSource();
            ctsCarpetas = c;
            foreach (TarjetaCarpeta t in carpetas) t.Detalle = "Calculando…";
            foreach (TarjetaCarpeta t in carpetas.ToList())
            {
                string ruta = t.Ruta;
                long tam;
                try
                {
                    tam = await Task.Run(() => Util.TamanoCarpeta(ruta, c.Token));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Util.Log("Archivos grandes: no se pudo medir " + ruta + ": " + ex.Message);
                    tam = -1;
                }
                if (IsDisposed || c.IsCancellationRequested) return;
                t.Tamano = tam;
                t.Detalle = tam >= 0 ? Util.FormatoTamano(tam) : "No disponible";
            }
        }

        // ------------------------------------------------------------------ búsqueda

        private async void Escanear()
        {
            if (cts != null) cts.Cancel();
            CancellationTokenSource miCts = new CancellationTokenSource();
            cts = miCts;
            int gen = ++generacion;
            long u = umbral;
            umbralEnCurso = u;
            escaneando = true;
            aviso = null;
            resultados = new List<ArchivoGrande>();
            tabla.Rows.Clear();
            lblDetalle.Text = "Preparando…";
            ActualizarVista();

            Progress<string> progreso = new Progress<string>(delegate(string carpeta)
            {
                if (gen != generacion || !escaneando || IsDisposed) return;
                lblDetalle.Text = "Revisando " + carpeta + "…";
            });
            List<string> extra = carpetas.Select(c => c.Ruta).ToList();
            List<ArchivoGrande> encontrados = new List<ArchivoGrande>();
            FinBusqueda resultado = FinBusqueda.Completa;
            try
            {
                await Task.Run(delegate { Buscar(u, extra, encontrados, progreso, miCts.Token); });
            }
            catch (OperationCanceledException)
            {
                resultado = FinBusqueda.Cancelada;
            }
            catch (Exception ex)
            {
                resultado = FinBusqueda.Error;
                Util.Log("Archivos grandes: error al buscar: " + ex);
            }

            // Si se lanzó otra búsqueda mientras tanto, esta ya no cuenta
            if (IsDisposed || gen != generacion) return;
            cts = null;
            escaneando = false;
            encontrados.Sort(delegate(ArchivoGrande a, ArchivoGrande b) { return b.Tamano.CompareTo(a.Tamano); });
            recortado = encontrados.Count > MaxResultados;
            if (recortado) encontrados.RemoveRange(MaxResultados, encontrados.Count - MaxResultados);
            resultados = encontrados;
            umbralEscaneado = u;
            fin = resultado;
            MostrarResultados();
            ActualizarVista();
        }

        /// <summary>Recorre las carpetas del perfil (sin AppData ni enlaces). Se ejecuta en segundo plano.</summary>
        private void Buscar(long u, List<string> extra, List<ArchivoGrande> encontrados, IProgress<string> progreso, CancellationToken ct)
        {
            Stopwatch reloj = Stopwatch.StartNew();
            long ultimo = -1000;
            List<string> raices = new List<string>();
            List<FileInfo> sueltos = new List<FileInfo>();
            try
            {
                foreach (FileSystemInfo i in new DirectoryInfo(perfil).GetFileSystemInfos())
                {
                    FileAttributes a;
                    try { a = i.Attributes; } catch { continue; }
                    if ((a & FileAttributes.ReparsePoint) != 0) continue;
                    if ((a & FileAttributes.Directory) != 0)
                    {
                        if (string.Equals(i.Name, "AppData", StringComparison.OrdinalIgnoreCase)) continue;
                        raices.Add(i.FullName);
                    }
                    else if ((a & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !Util.EsNube(a))
                    {
                        FileInfo f = i as FileInfo;
                        if (f != null) sueltos.Add(f);
                    }
                }
            }
            catch (Exception ex)
            {
                Util.Log("Archivos grandes: no se pudo leer el perfil: " + ex.Message);
            }

            // Carpetas conocidas que viven fuera del perfil (p. ej. Descargas movida a otro sitio)
            foreach (string r in extra)
            {
                if (DentroDe(r, perfil)) continue;
                bool cubierta = false;
                foreach (string x in raices)
                {
                    if (DentroDe(r, x)) { cubierta = true; break; }
                }
                if (!cubierta) raices.Add(r);
            }

            foreach (FileInfo f in sueltos) Considerar(f, u, encontrados);
            foreach (string raiz in raices)
            {
                ct.ThrowIfCancellationRequested();
                progreso.Report(Relativa(raiz));
                foreach (FileInfo f in Util.EnumerarArchivos(raiz, ct))
                {
                    long ahora = reloj.ElapsedMilliseconds;
                    if (ahora - ultimo >= 100)
                    {
                        ultimo = ahora;
                        progreso.Report(Relativa(f.DirectoryName));
                    }
                    Considerar(f, u, encontrados);
                }
            }
        }

        private void Considerar(FileInfo f, long u, List<ArchivoGrande> encontrados)
        {
            long largo;
            try { largo = f.Length; } catch { return; }
            if (largo < u) return;
            ArchivoGrande a = new ArchivoGrande();
            a.Nombre = f.Name;
            a.Ruta = f.FullName;
            a.Tamano = largo;
            a.Carpeta = Relativa(f.DirectoryName);
            try { a.Modificado = f.LastWriteTime; } catch { a.Modificado = DateTime.MinValue; }
            encontrados.Add(a);
        }

        private static bool DentroDe(string ruta, string carpeta)
        {
            if (string.IsNullOrEmpty(ruta) || string.IsNullOrEmpty(carpeta)) return false;
            string r = ruta.TrimEnd('\\'), c = carpeta.TrimEnd('\\');
            return string.Equals(r, c, StringComparison.OrdinalIgnoreCase) ||
                   r.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Ruta de carpeta relativa al perfil, como "~\Downloads".</summary>
        private string Relativa(string ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return "";
            string r = ruta.TrimEnd('\\');
            if (string.Equals(r, perfil, StringComparison.OrdinalIgnoreCase)) return "~";
            if (perfil.Length > 0 && r.StartsWith(perfil + "\\", StringComparison.OrdinalIgnoreCase)) return "~" + r.Substring(perfil.Length);
            return r;
        }

        // ------------------------------------------------------------------ tabla

        private void MostrarResultados()
        {
            ArchivoGrande sel = Seleccionado();
            string rutaSel = sel != null ? sel.Ruta : null;
            DataGridViewColumn orden = tabla.SortedColumn ?? colTamano;
            ListSortDirection dir = tabla.SortedColumn != null && tabla.SortOrder == SortOrder.Ascending
                ? ListSortDirection.Ascending : ListSortDirection.Descending;
            int primera = -1;
            try { if (tabla.Rows.Count > 0) primera = tabla.FirstDisplayedScrollingRowIndex; } catch { }

            tabla.Rows.Clear();
            List<DataGridViewRow> filas = new List<DataGridViewRow>();
            foreach (ArchivoGrande a in resultados)
            {
                if (a.Tamano < umbral) continue;
                DataGridViewRow r = new DataGridViewRow();
                r.CreateCells(tabla, a.Nombre, a.Carpeta, Util.FormatoTamano(a.Tamano),
                    a.Modificado == DateTime.MinValue ? "—" : a.Modificado.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                r.Cells[0].ToolTipText = a.Ruta;
                r.Cells[1].ToolTipText = Path.GetDirectoryName(a.Ruta);
                r.Tag = a;
                filas.Add(r);
            }
            if (filas.Count > 0)
            {
                tabla.Rows.AddRange(filas.ToArray());
                tabla.Sort(orden, dir);
            }
            QuitarSeleccion();

            // Solo se vuelve a marcar lo que el usuario ya había elegido
            if (rutaSel != null)
            {
                foreach (DataGridViewRow r in tabla.Rows)
                {
                    ArchivoGrande a = r.Tag as ArchivoGrande;
                    if (a != null && string.Equals(a.Ruta, rutaSel, StringComparison.OrdinalIgnoreCase))
                    {
                        tabla.CurrentCell = r.Cells[0];
                        r.Selected = true;
                        break;
                    }
                }
            }
            if (primera >= 0 && primera < tabla.Rows.Count)
            {
                try { tabla.FirstDisplayedScrollingRowIndex = primera; } catch { }
            }
        }

        private void QuitarSeleccion()
        {
            try { tabla.CurrentCell = null; } catch { }
            tabla.ClearSelection();
        }

        private void TablaSortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            ArchivoGrande a = tabla.Rows[e.RowIndex1].Tag as ArchivoGrande;
            ArchivoGrande b = tabla.Rows[e.RowIndex2].Tag as ArchivoGrande;
            if (a == null || b == null) return;
            if (e.Column == colTamano) e.SortResult = a.Tamano.CompareTo(b.Tamano);
            else if (e.Column == colFecha) e.SortResult = a.Modificado.CompareTo(b.Modificado);
            else e.SortResult = string.Compare(Convert.ToString(e.CellValue1), Convert.ToString(e.CellValue2), StringComparison.CurrentCultureIgnoreCase);
            if (e.SortResult == 0) e.SortResult = string.Compare(a.Ruta, b.Ruta, StringComparison.OrdinalIgnoreCase);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ acciones

        private void AbrirUbicacion()
        {
            ArchivoGrande a = Seleccionado();
            if (a == null) return;
            Util.AbrirUbicacion(a.Ruta);
        }

        private async void EnviarAPapelera()
        {
            ArchivoGrande a = Seleccionado();
            if (a == null || borrando || escaneando) return;
            string tam = Util.FormatoTamano(a.Tamano);
            if (!Dialogo.Confirmar(FindForm(), "¿Enviar a la papelera?",
                "“" + a.Nombre + "” (" + tam + ") irá a la papelera. Para liberar el espacio, vacía la papelera después.\n\n" +
                "Si es demasiado grande para la papelera, Windows te preguntará antes de borrarlo del todo.",
                "Enviar a la papelera", true))
            {
                return;
            }

            borrando = true;
            ActualizarBotones();
            try
            {
                // Se comprueba otra vez justo antes de tocar nada
                int estado = await Task.Run(() => Comprobar(a));
                if (IsDisposed) return;
                if (estado == 1)
                {
                    Quitar(a);
                    Dialogo.Info(FindForm(), "Ese archivo ya no está", "“" + a.Nombre + "” se movió o se borró desde que buscamos. Lo hemos quitado de la lista.");
                    return;
                }
                if (estado == 2)
                {
                    Dialogo.Info(FindForm(), "El archivo ha cambiado", "“" + a.Nombre + "” cambió desde que buscamos, así que no lo hemos tocado. Pulsa “Buscar de nuevo” y vuelve a intentarlo.");
                    return;
                }

                Exception error = await MoverAPapelera(a.Ruta);
                if (IsDisposed) return;
                bool sigue = await Task.Run(() => File.Exists(a.Ruta));
                if (IsDisposed) return;

                if (error != null && !(error is OperationCanceledException))
                {
                    Util.Log("Archivos grandes: no se pudo enviar a la papelera " + a.Ruta + ": " + error.Message);
                    Dialogo.Error(FindForm(), "No se pudo enviar a la papelera",
                        "“" + a.Nombre + "” sigue en su sitio. ¿Está abierto en otro programa? Ciérralo y vuelve a intentarlo.");
                }
                else if (sigue)
                {
                    // El usuario dijo que no en la ventana de Windows
                    aviso = "No se ha movido nada";
                    colorAviso = Tema.TextoTenue;
                    ActualizarVista();
                }
                else
                {
                    Util.Log("Archivos grandes: enviado a la papelera " + a.Ruta + " (" + tam + ")");
                    Quitar(a);
                    aviso = "“" + a.Nombre + "” está en la papelera. Vacíala para liberar " + tam + ".";
                    colorAviso = Tema.Exito;
                    ActualizarVista();
                }
            }
            catch (Exception ex)
            {
                Util.Log("Archivos grandes: error al enviar a la papelera " + a.Ruta + ": " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "No se pudo enviar a la papelera", "Algo salió mal. ¿Está abierto en otro programa?");
            }
            finally
            {
                borrando = false;
                if (!IsDisposed) ActualizarBotones();
            }
        }

        /// <summary>0 = todo igual, 1 = ya no existe, 2 = ha cambiado de tamaño.</summary>
        private static int Comprobar(ArchivoGrande a)
        {
            FileInfo fi = new FileInfo(a.Ruta);
            fi.Refresh();
            if (!fi.Exists) return 1;
            if (fi.Length != a.Tamano) return 2;
            return 0;
        }

        /// <summary>
        /// Manda el archivo a la papelera en un hilo STA (lo que pide el shell de Windows).
        /// Con AllDialogs, si el archivo no cabe en la papelera Windows pregunta antes de borrarlo del todo
        /// (con OnlyErrorDialogs lo borraría para siempre sin avisar).
        /// </summary>
        private static Task<Exception> MoverAPapelera(string ruta)
        {
            TaskCompletionSource<Exception> tcs = new TaskCompletionSource<Exception>();
            Thread hilo = new Thread(delegate()
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(ruta,
                        Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetResult(ex);
                }
            });
            hilo.SetApartmentState(ApartmentState.STA);
            hilo.IsBackground = true;
            hilo.Start();
            return tcs.Task;
        }

        private void Quitar(ArchivoGrande a)
        {
            resultados.Remove(a);
            foreach (DataGridViewRow r in tabla.Rows)
            {
                if (r.Tag == a)
                {
                    tabla.Rows.Remove(r);
                    break;
                }
            }
            // Que no quede marcada otra fila sin que el usuario la elija
            QuitarSeleccion();
            foreach (TarjetaCarpeta c in carpetas)
            {
                if (c.Tamano >= a.Tamano && DentroDe(a.Ruta, c.Ruta))
                {
                    c.Tamano -= a.Tamano;
                    c.Detalle = Util.FormatoTamano(c.Tamano);
                }
            }
            ActualizarVista();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (cts != null) cts.Cancel();
                if (ctsCarpetas != null) ctsCarpetas.Cancel();
                ayuda.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ controles propios

        /// <summary>Tarjeta pequeña de una carpeta: icono, nombre y tamaño. Al pulsarla se abre.</summary>
        private class TarjetaCarpeta : Tarjeta
        {
            public readonly string Nombre, Ruta;
            public long Tamano = -1;
            private string detalle = "Calculando…";

            public TarjetaCarpeta(string nombre, string ruta)
            {
                Nombre = nombre;
                Ruta = ruta;
                Cursor = Cursors.Hand;
                Padding = new Padding(Tema.S(14), 0, Tema.S(10), 0);
            }

            public string Detalle
            {
                get { return detalle; }
                set { detalle = value ?? ""; Invalidate(); }
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); BackColor = Tema.TarjetaHover; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); BackColor = Tema.Tarjeta; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                const TextFormatFlags linea = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                                              TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
                Font f1 = Tema.FuenteSemibold(9.5f), f2 = Tema.Fuente(9f);
                int x = Padding.Left;
                int lado = Tema.S(32);
                // En tarjetas muy estrechas se quita el icono para dejar sitio al texto
                if (Width - Padding.Horizontal - lado - Tema.S(12) >= Tema.S(56))
                {
                    Rectangle chip = new Rectangle(x, (Height - lado) / 2, lado, lado);
                    using (GraphicsPath p = Tema.Redondeado(chip, Tema.S(8)))
                    using (SolidBrush b = new SolidBrush(Tema.Seleccion))
                    {
                        g.FillPath(b, p);
                    }
                    TextRenderer.DrawText(g, Tema.IcoArchivos, Tema.FuenteIconos(12f), chip, Tema.Acento,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    x = chip.Right + Tema.S(12);
                }
                int w = Math.Max(1, Width - x - Padding.Right);
                int h1 = TextRenderer.MeasureText(g, "Ág", f1, Size.Empty, linea).Height;
                int h2 = TextRenderer.MeasureText(g, "Ág", f2, Size.Empty, linea).Height;
                int y = (Height - h1 - h2 - Tema.S(2)) / 2;
                TextRenderer.DrawText(g, Nombre, f1, new Rectangle(x, y, w, h1), Tema.Texto, linea);
                TextRenderer.DrawText(g, detalle, f2, new Rectangle(x, y + h1 + Tema.S(2), w, h2), Tema.TextoSuave, linea);
            }
        }

        /// <summary>Mensaje centrado (icono, título y texto) para cuando la tabla está vacía o cargando.</summary>
        private class EstadoVacio : Control
        {
            private string glifo = Tema.IcoBuscar, titulo = "", texto = "";
            private Color colorGlifo = Tema.TextoTenue;

            public EstadoVacio()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw, true);
            }

            public void Establecer(string glifo, Color color, string titulo, string texto)
            {
                this.glifo = glifo ?? "";
                colorGlifo = color;
                this.titulo = titulo ?? "";
                this.texto = texto ?? "";
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                int ancho = Math.Min(Width - Tema.S(40), Tema.S(440));
                if (ancho <= 0) return;
                const TextFormatFlags centro = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                               TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
                Font fg = Tema.FuenteIconos(24f), ft = Tema.FuenteSemibold(12f), fx = Tema.Fuente(10f);
                int hg = TextRenderer.MeasureText(g, glifo, fg, Size.Empty, TextFormatFlags.NoPadding).Height;
                int ht = TextRenderer.MeasureText(g, titulo, ft, new Size(ancho, int.MaxValue), centro).Height;
                int hx = texto.Length == 0 ? 0 : TextRenderer.MeasureText(g, texto, fx, new Size(ancho, int.MaxValue), centro).Height;
                int total = hg + Tema.S(14) + ht + (hx > 0 ? Tema.S(6) + hx : 0);
                int y = Math.Max(0, (Height - total) / 2 - Tema.S(12));
                int x = (Width - ancho) / 2;
                TextRenderer.DrawText(g, glifo, fg, new Rectangle(0, y, Width, hg), colorGlifo,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                y += hg + Tema.S(14);
                TextRenderer.DrawText(g, titulo, ft, new Rectangle(x, y, ancho, ht), Tema.Texto, centro);
                y += ht + Tema.S(6);
                if (hx > 0) TextRenderer.DrawText(g, texto, fx, new Rectangle(x, y, ancho, hx), Tema.TextoSuave, centro);
            }
        }
    }
}
