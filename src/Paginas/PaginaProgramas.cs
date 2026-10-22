using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OptimizadorPC
{
    /// <summary>
    /// Programas instalados (leídos del registro, igual que "Programas y características")
    /// con acceso a su desinstalador. Leer la lista nunca cambia nada.
    /// </summary>
    class PaginaProgramas : PaginaBase
    {
        private const string ClaveDesinstalar = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string IcoPapelera = "\uE74D";
        private const string IcoAbrirFuera = "\uE8A7";
        private const int EsperaMaxima = 60 * 60 * 1000;   // una hora como mucho esperando al desinstalador
        private const int ErrorCancelado = 1223;           // el usuario dijo "No" al aviso de permisos de Windows

        private static readonly Regex RegexGuid = new Regex(
            @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");

        // Los iconos se guardan entre recargas para no extraerlos otra vez
        private static readonly Dictionary<string, Bitmap> cacheIconos = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Un programa leído de una clave Uninstall del registro.</summary>
        private class Programa
        {
            public string Nombre = "", Editor = "", Version = "", Icono = "";
            public long Tamano = -1;
            public DateTime? Fecha;
            public RegistryHive Colmena;
            public RegistryView Vista;
            public string Subclave;
            public Bitmap Imagen;
        }

        private class ResultadoLanzar
        {
            public bool Abierto, Cancelado, YaNoEsta;
            public string Error;
        }

        private readonly CajaTexto buscador;
        private readonly Label lblResumen, lblSeleccion;
        private readonly DataGridView tabla;
        private readonly DataGridViewImageColumn colIcono;
        private readonly DataGridViewTextBoxColumn colNombre, colEditor, colVersion, colTamano, colFecha;
        private readonly PanelEstado estado;
        private readonly Boton btnDesinstalar, btnActualizar, btnPanel;
        private readonly Bitmap iconoGenerico;

        private List<Programa> todos = new List<Programa>();
        private bool cargando, cargado, desinstalando, llenando;
        private string aviso;
        private DataGridViewColumn columnaOrden;
        private ListSortDirection direccionOrden = ListSortDirection.Descending;

        public PaginaProgramas() : base("Programas", "Desinstala lo que ya no uses")
        {
            iconoGenerico = CrearIconoGenerico(Tema.S(20));

            // ---- Botones del encabezado ----
            btnActualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnActualizar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnActualizar.Click += delegate { aviso = null; Cargar(); };
            btnPanel = new Boton("Programas y características", EstiloBoton.Fantasma, IcoAbrirFuera);
            btnPanel.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnPanel.Click += delegate { AbrirPanelClasico(); };
            Acciones.Controls.Add(btnActualizar);
            Acciones.Controls.Add(btnPanel);

            // ---- Barra superior: buscador y resumen ----
            buscador = new CajaTexto("Buscar programa…", Tema.IcoBuscar);
            buscador.Width = Tema.S(320);
            buscador.Margin = new Padding(0);
            buscador.Anchor = AnchorStyles.Left;
            buscador.TextoCambiado += delegate { if (cargado) LlenarTabla(false); };

            lblResumen = Tema.Etiqueta("", 9.5f, Tema.TextoSuave);
            lblResumen.Anchor = AnchorStyles.Right;
            lblResumen.Margin = new Padding(Tema.S(16), 0, Tema.S(4), 0);

            TableLayoutPanel arriba = NuevaFila(new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100));
            arriba.Margin = new Padding(0, 0, 0, Tema.S(16));
            arriba.Controls.Add(buscador, 0, 0);
            arriba.Controls.Add(lblResumen, 1, 0);

            // ---- Tabla ----
            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = true;
            tabla.Margin = new Padding(0);

            colIcono = new DataGridViewImageColumn();
            colIcono.HeaderText = "";
            colIcono.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colIcono.Width = Tema.S(44);
            colIcono.Resizable = DataGridViewTriState.False;
            colIcono.SortMode = DataGridViewColumnSortMode.NotSortable;
            colIcono.ImageLayout = DataGridViewImageCellLayout.Normal;
            colIcono.DefaultCellStyle.NullValue = null;
            colIcono.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colIcono.DefaultCellStyle.Padding = new Padding(Tema.S(8), 0, 0, 0);

            colNombre = NuevaColumna("Programa", 36, 160);
            colNombre.DefaultCellStyle.Font = Tema.FuenteSemibold(9.5f);

            colEditor = NuevaColumna("Editor", 24, 110);
            colEditor.DefaultCellStyle.ForeColor = Tema.TextoSuave;

            colVersion = NuevaColumna("Versión", 13, 90);
            colVersion.DefaultCellStyle.ForeColor = Tema.TextoTenue;
            colVersion.DefaultCellStyle.Font = Tema.Fuente(9f);

            colTamano = NuevaColumna("Tamaño", 12, 100);
            colTamano.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colTamano.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;

            colFecha = NuevaColumna("Instalado", 13, 108);
            colFecha.DefaultCellStyle.ForeColor = Tema.TextoSuave;

            tabla.Columns.AddRange(new DataGridViewColumn[] { colIcono, colNombre, colEditor, colVersion, colTamano, colFecha });
            tabla.SortCompare += OrdenarFilas;
            tabla.Sorted += delegate
            {
                if (tabla.SortedColumn == null) return;
                columnaOrden = tabla.SortedColumn;
                direccionOrden = tabla.SortOrder == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
            };
            tabla.CellFormatting += FormatearCelda;
            tabla.SelectionChanged += delegate
            {
                if (!llenando) aviso = null;
                ActualizarSeleccion();
            };

            estado = new PanelEstado();

            Tarjeta tarjeta = new Tarjeta();
            tarjeta.Dock = DockStyle.Fill;
            tarjeta.Margin = new Padding(0);
            tarjeta.Padding = Tema.S(12, 6, 12, 12);
            tarjeta.Controls.Add(tabla);
            tarjeta.Controls.Add(estado);

            // ---- Barra inferior: programa elegido y botón Desinstalar ----
            lblSeleccion = new Label();
            lblSeleccion.AutoSize = false;
            lblSeleccion.Dock = DockStyle.Fill;
            lblSeleccion.TextAlign = ContentAlignment.MiddleLeft;
            lblSeleccion.AutoEllipsis = true;
            lblSeleccion.UseMnemonic = false;
            lblSeleccion.Font = Tema.Fuente(9.5f);
            lblSeleccion.ForeColor = Tema.TextoSuave;
            lblSeleccion.Margin = new Padding(Tema.S(4), 0, Tema.S(16), 0);

            btnDesinstalar = new Boton("Desinstalar", EstiloBoton.Peligro, IcoPapelera);
            btnDesinstalar.Margin = new Padding(0);
            btnDesinstalar.Enabled = false;
            btnDesinstalar.Click += Desinstalar_Click;

            TableLayoutPanel abajo = NuevaFila(new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
            abajo.Margin = new Padding(0, Tema.S(16), 0, 0);
            abajo.Controls.Add(lblSeleccion, 0, 0);
            abajo.Controls.Add(btnDesinstalar, 1, 0);

            // ---- Todo junto ----
            TableLayoutPanel cuerpo = new TableLayoutPanel();
            cuerpo.Dock = DockStyle.Fill;
            cuerpo.Margin = new Padding(0);
            cuerpo.Padding = new Padding(0);
            cuerpo.ColumnCount = 1;
            cuerpo.RowCount = 3;
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cuerpo.Controls.Add(arriba, 0, 0);
            cuerpo.Controls.Add(tarjeta, 0, 1);
            cuerpo.Controls.Add(abajo, 0, 2);
            Contenido.Controls.Add(cuerpo);

            MostrarEstado();
            estado.Cargando("Buscando programas instalados…");
            ActualizarSeleccion();
        }

        public override void AlMostrar()
        {
            // Solo lee el registro, y solo la primera vez (luego con Actualizar o tras desinstalar)
            if (!cargado && !cargando) Cargar();
        }

        // ------------------------------------------------------------------ construcción

        private static TableLayoutPanel NuevaFila(params ColumnStyle[] columnas)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Margin = new Padding(0);
            t.Padding = new Padding(0);
            t.RowCount = 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.ColumnCount = columnas.Length;
            foreach (ColumnStyle c in columnas) t.ColumnStyles.Add(c);
            return t;
        }

        private static DataGridViewTextBoxColumn NuevaColumna(string titulo, float peso, int minimo)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.FillWeight = peso;
            c.MinimumWidth = Tema.S(minimo);
            c.SortMode = DataGridViewColumnSortMode.Automatic;
            c.ReadOnly = true;
            return c;
        }

        // Cuadradito gris con el icono de "programa" para los que no traen icono propio
        private static Bitmap CrearIconoGenerico(int lado)
        {
            Bitmap b = new Bitmap(lado, lado);
            using (Graphics g = Graphics.FromImage(b))
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, lado - 1, lado - 1), lado * 0.25f))
            using (SolidBrush fondo = new SolidBrush(Tema.Control))
            using (SolidBrush texto = new SolidBrush(Tema.TextoSuave))
            using (Font f = new Font("Segoe MDL2 Assets", lado * 0.5f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.FillPath(fondo, p);
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Tema.IcoProgramas, f, texto, new RectangleF(0, 0, lado, lado), sf);
            }
            return b;
        }

        private void MostrarTabla()
        {
            estado.Detener();
            estado.Visible = false;
            tabla.Visible = true;
        }

        private void MostrarEstado()
        {
            tabla.Visible = false;
            estado.Visible = true;
        }

        // ------------------------------------------------------------------ carga

        private async void Cargar()
        {
            await CargarAsync();
        }

        private async Task CargarAsync()
        {
            if (cargando) return;
            cargando = true;
            btnActualizar.Enabled = false;
            if (!cargado)
            {
                MostrarEstado();
                estado.Cargando("Buscando programas instalados…");
            }
            else
            {
                lblResumen.Text = "Actualizando…";
            }
            try
            {
                List<Programa> lista = await Task.Run(() => LeerProgramas());
                if (IsDisposed) return;
                todos = lista;
                cargado = true;
                LlenarTabla(true);
                CargarIconos(lista);
            }
            catch (Exception ex)
            {
                Util.Log("Programas: no se pudo leer la lista: " + ex);
                if (IsDisposed) return;
                if (!cargado)
                {
                    MostrarEstado();
                    estado.Mensaje(Tema.IcoAviso, Tema.Peligro, "No se pudo leer la lista de programas",
                        "Pulsa Actualizar para intentarlo otra vez.");
                    lblResumen.Text = "";
                }
                else
                {
                    LlenarTabla(true);
                    Dialogo.Error(FindForm(), "No se pudo actualizar la lista", "Vuelve a intentarlo en un momento.");
                }
            }
            finally
            {
                cargando = false;
                if (!IsDisposed) btnActualizar.Enabled = true;
            }
        }

        private async void CargarIconos(List<Programa> lista)
        {
            try
            {
                int lado = Tema.S(20);
                await Task.Run(() =>
                {
                    foreach (Programa p in lista) p.Imagen = IconoDe(p.Icono, lado);
                });
                if (IsDisposed) return;
                tabla.InvalidateColumn(colIcono.Index);
            }
            catch (Exception ex)
            {
                Util.Log("Programas: error al cargar iconos: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ registro (solo lectura)

        private static List<Programa> LeerProgramas()
        {
            Dictionary<string, Programa> porNombre = new Dictionary<string, Programa>(StringComparer.OrdinalIgnoreCase);
            LeerRama(porNombre, RegistryHive.LocalMachine, RegistryView.Registry64);
            if (Environment.Is64BitOperatingSystem) LeerRama(porNombre, RegistryHive.LocalMachine, RegistryView.Registry32);
            LeerRama(porNombre, RegistryHive.CurrentUser, RegistryView.Registry64);
            return new List<Programa>(porNombre.Values);
        }

        private static void LeerRama(Dictionary<string, Programa> porNombre, RegistryHive colmena, RegistryView vista)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, vista))
                using (RegistryKey raiz = b.OpenSubKey(ClaveDesinstalar))
                {
                    if (raiz == null) return;
                    foreach (string sub in raiz.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = raiz.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                Programa p = LeerEntrada(k);
                                if (p == null) continue;
                                p.Colmena = colmena;
                                p.Vista = vista;
                                p.Subclave = sub;

                                // Mismo nombre en varias claves: nos quedamos con la que dice su tamaño
                                Programa existente;
                                if (!porNombre.TryGetValue(p.Nombre, out existente) || (existente.Tamano <= 0 && p.Tamano > 0))
                                {
                                    porNombre[p.Nombre] = p;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Util.Log("Programas: no se pudo leer " + colmena + " (" + vista + "): " + ex.Message);
            }
        }

        private static Programa LeerEntrada(RegistryKey k)
        {
            string nombre = Texto(k, "DisplayName");
            string desinstalar = Texto(k, "UninstallString");
            if (nombre.Length == 0 || desinstalar.Length == 0) return null;
            if (Numero(k, "SystemComponent") == 1) return null;
            if (Texto(k, "ParentKeyName").Length > 0) return null;
            string tipo = Texto(k, "ReleaseType");
            if (tipo.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                tipo.Equals("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                tipo.Equals("Security Update", StringComparison.OrdinalIgnoreCase)) return null;

            Programa p = new Programa();
            p.Nombre = nombre;
            p.Editor = Texto(k, "Publisher");
            p.Version = Texto(k, "DisplayVersion");
            p.Icono = Texto(k, "DisplayIcon");
            long kb = Numero(k, "EstimatedSize");
            p.Tamano = kb > 0 ? kb * 1024 : -1;
            p.Fecha = LeerFecha(Texto(k, "InstallDate"));
            return p;
        }

        private static string Texto(RegistryKey k, string valor)
        {
            try
            {
                object v = k.GetValue(valor);
                if (v == null) return "";
                string[] varias = v as string[];
                if (varias != null) return string.Join(" ", varias).Trim();
                return Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
            }
            catch { return ""; }
        }

        private static long Numero(RegistryKey k, string valor)
        {
            try
            {
                object v = k.GetValue(valor);
                if (v is int) return (long)(uint)(int)v;   // los DWORD llegan como int con signo
                if (v is long) return (long)v;
                string s = v as string;
                long n;
                if (s != null && long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            }
            catch { }
            return 0;
        }

        private static DateTime? LeerFecha(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] formatos = { "yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "d/M/yyyy" };
            DateTime d;
            if (DateTime.TryParseExact(s.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) &&
                d.Year >= 1990 && d <= DateTime.Now.AddDays(1))
            {
                return d;
            }
            return null;
        }

        // ------------------------------------------------------------------ iconos

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string archivo, int indice, IntPtr[] grandes, IntPtr[] pequenos, uint cantidad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icono);

        private static Bitmap IconoDe(string displayIcon, int lado)
        {
            if (string.IsNullOrEmpty(displayIcon)) return null;
            string clave = displayIcon + "|" + lado;
            Bitmap b;
            lock (cacheIconos)
            {
                if (cacheIconos.TryGetValue(clave, out b)) return b;
            }
            try { b = ExtraerIcono(displayIcon, lado); }
            catch { b = null; }
            lock (cacheIconos)
            {
                cacheIconos[clave] = b;
            }
            return b;
        }

        // DisplayIcon suele ser "C:\ruta\programa.exe,0" (con o sin comillas)
        private static Bitmap ExtraerIcono(string displayIcon, int lado)
        {
            string ruta = displayIcon.Trim();
            int indice = 0;
            int coma = ruta.LastIndexOf(',');
            if (coma > 0)
            {
                int n;
                if (int.TryParse(ruta.Substring(coma + 1).Trim(), out n))
                {
                    indice = n;
                    ruta = ruta.Substring(0, coma);
                }
            }
            ruta = Environment.ExpandEnvironmentVariables(ruta.Trim().Trim('"').Trim());
            if (ruta.Length < 4 || !Path.IsPathRooted(ruta) || !File.Exists(ruta)) return null;

            IntPtr[] grandes = new IntPtr[1];
            if (ExtractIconEx(ruta, indice, grandes, null, 1) == 0 || grandes[0] == IntPtr.Zero) return null;
            try
            {
                using (Icon ic = Icon.FromHandle(grandes[0]))
                using (Bitmap origen = ic.ToBitmap())
                {
                    Bitmap b = new Bitmap(lado, lado);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawImage(origen, new Rectangle(0, 0, lado, lado));
                    }
                    return b;
                }
            }
            finally
            {
                DestroyIcon(grandes[0]);
            }
        }

        private void FormatearCelda(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colIcono.Index) return;
            Programa p = tabla.Rows[e.RowIndex].Tag as Programa;
            e.Value = (p != null && p.Imagen != null) ? p.Imagen : iconoGenerico;
            e.FormattingApplied = true;
        }

        // ------------------------------------------------------------------ tabla

        private static bool Coincide(Programa p, string filtro)
        {
            if (filtro.Length == 0) return true;
            CompareInfo ci = CultureInfo.CurrentCulture.CompareInfo;
            CompareOptions op = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            return ci.IndexOf(p.Nombre, filtro, op) >= 0 || ci.IndexOf(p.Editor, filtro, op) >= 0;
        }

        private void LlenarTabla(bool conservarPosicion)
        {
            string filtro = buscador.Text.Trim();
            Programa antes = Seleccionado();
            string nombreAntes = antes != null ? antes.Nombre : null;
            int primera = -1;
            if (conservarPosicion && tabla.Rows.Count > 0)
            {
                try { primera = tabla.FirstDisplayedScrollingRowIndex; } catch { primera = -1; }
            }

            long total = 0;
            llenando = true;
            try
            {
                tabla.Rows.Clear();
                List<DataGridViewRow> filas = new List<DataGridViewRow>();
                foreach (Programa p in todos)
                {
                    if (p.Tamano > 0) total += p.Tamano;
                    if (!Coincide(p, filtro)) continue;
                    DataGridViewRow r = (DataGridViewRow)tabla.RowTemplate.Clone();
                    r.CreateCells(tabla, null, p.Nombre,
                        p.Editor.Length > 0 ? p.Editor : "—",
                        p.Version.Length > 0 ? p.Version : "—",
                        p.Tamano > 0 ? Util.FormatoTamano(p.Tamano) : "—",
                        p.Fecha.HasValue ? p.Fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—");
                    r.Tag = p;
                    filas.Add(r);
                }
                tabla.Rows.AddRange(filas.ToArray());
                if (columnaOrden == null) columnaOrden = colTamano;
                tabla.Sort(columnaOrden, direccionOrden);

                if (tabla.Rows.Count > 0) MostrarTabla();
                else MostrarEstado();

                // Nada elegido salvo que ya lo estuviera antes de recargar
                tabla.ClearSelection();
                tabla.CurrentCell = null;
                if (nombreAntes != null)
                {
                    foreach (DataGridViewRow r in tabla.Rows)
                    {
                        Programa p = r.Tag as Programa;
                        if (p != null && string.Equals(p.Nombre, nombreAntes, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                tabla.CurrentCell = r.Cells[colNombre.Index];
                                r.Selected = true;
                            }
                            catch { }
                            break;
                        }
                    }
                }
                if (primera >= 0 && tabla.Rows.Count > 0)
                {
                    try { tabla.FirstDisplayedScrollingRowIndex = Math.Min(primera, tabla.Rows.Count - 1); } catch { }
                }
            }
            finally
            {
                llenando = false;
            }

            int n = todos.Count;
            if (tabla.Rows.Count == 0)
            {
                if (n == 0)
                {
                    estado.Mensaje(Tema.IcoInfo, Tema.TextoSuave, "No se encontraron programas", "Pulsa Actualizar para buscar otra vez.");
                }
                else
                {
                    estado.Mensaje(Tema.IcoBuscar, Tema.TextoSuave, "Ningún programa coincide con tu búsqueda",
                        "Prueba con otra palabra o borra la búsqueda.");
                }
            }

            if (filtro.Length > 0)
            {
                lblResumen.Text = "Mostrando " + tabla.Rows.Count + " de " + n + (n == 1 ? " programa" : " programas");
            }
            else
            {
                string texto = n == 1 ? "1 programa" : n + " programas";
                if (total > 0) texto += "  ·  " + Util.FormatoTamano(total) + " en total (aprox.)";
                lblResumen.Text = texto;
            }
            ActualizarSeleccion();
        }

        private static int Comparar(string a, string b)
        {
            // Los vacíos van al final al ordenar de la A a la Z
            bool va = string.IsNullOrEmpty(a), vb = string.IsNullOrEmpty(b);
            if (va || vb) return va == vb ? 0 : (va ? 1 : -1);
            return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
        }

        private static int CompararVersion(string a, string b)
        {
            Version x, y;
            if (Version.TryParse(a, out x) && Version.TryParse(b, out y)) return x.CompareTo(y);
            return Comparar(a, b);
        }

        // Se ordena por los valores reales (bytes, fechas), no por el texto que se ve
        private void OrdenarFilas(object sender, DataGridViewSortCompareEventArgs e)
        {
            Programa a = tabla.Rows[e.RowIndex1].Tag as Programa;
            Programa b = tabla.Rows[e.RowIndex2].Tag as Programa;
            if (a == null || b == null) return;
            int r;
            if (e.Column == colTamano) r = a.Tamano.CompareTo(b.Tamano);
            else if (e.Column == colFecha) r = (a.Fecha ?? DateTime.MinValue).CompareTo(b.Fecha ?? DateTime.MinValue);
            else if (e.Column == colEditor) r = Comparar(a.Editor, b.Editor);
            else if (e.Column == colVersion) r = CompararVersion(a.Version, b.Version);
            else r = 0;
            if (r == 0) r = Comparar(a.Nombre, b.Nombre);
            e.SortResult = r;
            e.Handled = true;
        }

        private Programa Seleccionado()
        {
            if (tabla.SelectedRows.Count == 0) return null;
            return tabla.SelectedRows[0].Tag as Programa;
        }

        private void ActualizarSeleccion()
        {
            Programa p = Seleccionado();
            btnDesinstalar.Enabled = p != null && !desinstalando;
            if (desinstalando) return;   // mientras tanto se ve el mensaje de espera
            if (aviso != null)
            {
                lblSeleccion.Text = aviso;
                lblSeleccion.ForeColor = Tema.Aviso;
            }
            else if (p == null)
            {
                lblSeleccion.Text = "Elige un programa de la lista para desinstalarlo.";
                lblSeleccion.ForeColor = Tema.TextoSuave;
            }
            else
            {
                lblSeleccion.Text = p.Nombre + (p.Tamano > 0 ? "  ·  " + Util.FormatoTamano(p.Tamano) : "");
                lblSeleccion.ForeColor = Tema.Texto;
            }
        }

        private void AbrirPanelClasico()
        {
            if (!Util.Abrir(Util.RutaSistema("control.exe"), "appwiz.cpl"))
            {
                Dialogo.Error(FindForm(), "No se pudo abrir", "Windows no pudo abrir Programas y características.");
            }
        }

        // ------------------------------------------------------------------ desinstalar

        private async void Desinstalar_Click(object sender, EventArgs e)
        {
            Programa p = Seleccionado();
            if (p == null || desinstalando) return;
            string nombre = p.Nombre;
            try
            {
                if (!Dialogo.Confirmar(FindForm(), "¿Desinstalar " + nombre + "?",
                    "Se abrirá el desinstalador de " + nombre + ". Sigue sus pasos para terminar.\n\n" +
                    "Cuando acabe, buscaremos lo que haya dejado (carpetas, registro y accesos directos) para que puedas borrarlo.",
                    "Abrir desinstalador", true)) return;

                desinstalando = true;
                aviso = null;
                btnDesinstalar.Enabled = false;
                lblSeleccion.ForeColor = Tema.TextoSuave;
                lblSeleccion.Text = "Esperando a que termines con el desinstalador de " + nombre + "…";

                // Se guarda lo que sabemos del programa ANTES de desinstalarlo: luego su entrada desaparece
                DatosPrograma datos = await Task.Run(() => Residuos.Leer(p.Colmena, p.Vista, p.Subclave));
                ResultadoLanzar r = await Task.Run(() => AbrirDesinstalador(p));
                if (IsDisposed) return;

                bool forzar = false;
                if (r.Cancelado)
                {
                    Dialogo.Info(FindForm(), "No se abrió el desinstalador",
                        "Para desinstalar " + nombre + " tienes que aceptar el aviso de permisos de Windows.");
                }
                else if (r.YaNoEsta)
                {
                    Dialogo.Info(FindForm(), "Ya no está instalado", nombre + " ya no aparece en Windows. La lista se actualizará.");
                }
                else if (!r.Abierto)
                {
                    // Sin desinstalador: se ofrece quitarlo "a la fuerza" (borrar sus restos y su entrada)
                    forzar = datos != null && Dialogo.Confirmar(FindForm(), "No se pudo abrir el desinstalador",
                        (r.Error ?? "Windows no pudo abrirlo.") + "\n\n¿Quieres quitarlo igualmente? Buscaremos sus carpetas y su configuración " +
                        "y podrás elegir qué borrar. También desaparecerá de la lista de programas.", "Buscar restos", true);
                }

                // Un momento para que Windows termine de actualizar el registro
                if (r.Abierto) await Task.Delay(1500);
                if (IsDisposed) return;
                await CargarAsync();
                if (IsDisposed) return;
                bool sigue = todos.Exists(x => string.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
                if (r.Abierto && sigue)
                {
                    aviso = nombre + " sigue en la lista. Si su desinstalador aún está trabajando, pulsa Actualizar cuando acabe.";
                }
                else if ((r.Abierto && !sigue) || forzar)
                {
                    await BuscarYBorrarRestos(datos, forzar);
                }
                desinstalando = false;
            }
            catch (Exception ex)
            {
                Util.Log("Programas: error al desinstalar '" + nombre + "': " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "Algo salió mal", "No se pudo abrir el desinstalador de " + nombre + ".");
            }
            finally
            {
                desinstalando = false;
                if (!IsDisposed) ActualizarSeleccion();
            }
        }

        /// <summary>Busca lo que dejó el programa, deja elegir qué borrar y lo borra.</summary>
        private async Task BuscarYBorrarRestos(DatosPrograma datos, bool forzado)
        {
            if (datos == null || IsDisposed) return;
            lblSeleccion.ForeColor = Tema.TextoSuave;
            lblSeleccion.Text = "Buscando restos de " + datos.Nombre + "…";
            List<Resto> restos = await Task.Run(() => Residuos.Buscar(datos, forzado, System.Threading.CancellationToken.None));
            if (IsDisposed) return;
            if (restos.Count == 0)
            {
                Dialogo.Exito(FindForm(), "Desinstalado por completo", datos.Nombre + " se desinstaló y no dejó restos en tu PC.");
                return;
            }

            List<Resto> elegidos = VentanaRestos.Elegir(FindForm(), datos.Nombre, restos);
            if (elegidos == null)
            {
                aviso = "No se borraron los restos de " + datos.Nombre + ".";
                return;
            }
            lblSeleccion.Text = "Borrando restos de " + datos.Nombre + "…";
            ResultadoBorradoRestos res = await Task.Run(() => Residuos.Borrar(elegidos, datos.Nombre));
            if (IsDisposed) return;

            if (res.Fallidos == 0)
            {
                Dialogo.Exito(FindForm(), "Restos borrados",
                    "Se borraron " + res.Borrados + (res.Borrados == 1 ? " resto" : " restos") + " de " + datos.Nombre +
                    (res.Bytes > 0 ? " (" + Util.FormatoTamano(res.Bytes) + ")" : "") + ". Las carpetas están en la papelera" +
                    (res.CarpetaRespaldo != null ? " y hay una copia del registro en la carpeta de respaldos de la app." : "."));
            }
            else
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("Se borraron ").Append(res.Borrados).Append(", pero estos no:\n\n");
                for (int i = 0; i < res.Errores.Count && i < 6; i++) sb.Append("•  ").Append(Util.Recortar(res.Errores[i], 110)).Append('\n');
                sb.Append("\nSi alguno está en uso, cierra el programa o reinicia el PC y vuelve a intentarlo.");
                Dialogo.Error(FindForm(), "Algunos restos no se pudieron borrar", sb.ToString());
            }
            if (forzado && !IsDisposed) await CargarAsync();
        }

        // Se vuelve a leer del registro justo antes de actuar, por si cambió mientras tanto
        private static string LeerComandoActual(Programa p)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(p.Colmena, p.Vista))
                using (RegistryKey k = b.OpenSubKey(ClaveDesinstalar + "\\" + p.Subclave))
                {
                    if (k == null) return null;
                    if (!string.Equals(Texto(k, "DisplayName"), p.Nombre, StringComparison.OrdinalIgnoreCase)) return null;
                    string c = Texto(k, "UninstallString");
                    return c.Length > 0 ? c : null;
                }
            }
            catch { return null; }
        }

        /// <summary>Abre el desinstalador normal (nunca el silencioso) y espera a que se cierre. Bloquea.</summary>
        private static ResultadoLanzar AbrirDesinstalador(Programa p)
        {
            ResultadoLanzar r = new ResultadoLanzar();
            string comando = LeerComandoActual(p);
            if (comando == null)
            {
                r.YaNoEsta = true;
                Util.Log("Programas: '" + p.Nombre + "' ya no está en el registro; no se hace nada");
                return r;
            }

            string cmd = Environment.ExpandEnvironmentVariables(comando).Trim();
            string exe, args;
            Match guid = RegexGuid.Match(cmd);
            if (cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0 && guid.Success)
            {
                // Instalado con MSI: /x desinstala (a veces el registro trae /I, que solo "modifica")
                exe = Util.RutaSistema("msiexec.exe");
                args = "/x " + guid.Value;
            }
            else
            {
                string problema;
                if (!PartirComando(cmd, out exe, out args, out problema))
                {
                    if (problema != null)
                    {
                        r.Error = problema;
                        Util.Log("Programas: no se encontró el desinstalador de '" + p.Nombre + "': " + cmd);
                        return r;
                    }
                    return ConCmd(p, cmd);
                }
            }

            Util.Log("Programas: abriendo el desinstalador de '" + p.Nombre + "': " + exe + " " + args);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.Arguments = args;
                psi.UseShellExecute = true;   // así se ve su propia ventana y pide permisos si los necesita
                string carpeta = null;
                try { carpeta = Path.GetDirectoryName(exe); } catch { carpeta = null; }
                if (!string.IsNullOrEmpty(carpeta) && Directory.Exists(carpeta)) psi.WorkingDirectory = carpeta;

                using (Process proc = Process.Start(psi))
                {
                    r.Abierto = true;
                    if (proc != null)
                    {
                        if (proc.WaitForExit(EsperaMaxima))
                        {
                            string codigo = "?";
                            try { codigo = proc.ExitCode.ToString(CultureInfo.InvariantCulture); } catch { }
                            Util.Log("Programas: el desinstalador de '" + p.Nombre + "' terminó (código " + codigo + ")");
                        }
                        else
                        {
                            Util.Log("Programas: el desinstalador de '" + p.Nombre + "' sigue abierto tras una hora; se deja de esperar");
                        }
                    }
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == ErrorCancelado)
                {
                    r.Cancelado = true;
                    Util.Log("Programas: se canceló el permiso de administrador para '" + p.Nombre + "'");
                }
                else
                {
                    r.Error = "Windows no pudo abrirlo: " + ex.Message;
                    Util.Log("Programas: error al abrir el desinstalador de '" + p.Nombre + "': " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                r.Error = "Windows no pudo abrirlo: " + ex.Message;
                Util.Log("Programas: error al abrir el desinstalador de '" + p.Nombre + "': " + ex);
            }
            return r;
        }

        // Último recurso si no se entiende el comando: que lo ejecute cmd tal cual
        private static ResultadoLanzar ConCmd(Programa p, string cmd)
        {
            ResultadoLanzar r = new ResultadoLanzar();
            Util.Log("Programas: abriendo con cmd el desinstalador de '" + p.Nombre + "': " + cmd);
            ResultadoProceso rp = Util.EjecutarProceso(Util.RutaSistema("cmd.exe"), "/c \"" + cmd + "\"", EsperaMaxima);
            Util.Log("Programas: cmd terminó con código " + rp.Codigo + " para '" + p.Nombre + "'");
            if (rp.Codigo == 9009 || rp.Codigo == 3)
            {
                r.Error = "No se encontró su desinstalador. Puede que el programa esté medio borrado: prueba desde Programas y características.";
            }
            else
            {
                r.Abierto = true;
            }
            return r;
        }

        /// <summary>Separa "programa.exe argumentos". Devuelve false si no se entiende o no existe el programa.</summary>
        private static bool PartirComando(string cmd, out string exe, out string args, out string problema)
        {
            exe = null;
            args = "";
            problema = null;
            string s = cmd.Trim();
            if (s.Length == 0) return false;

            if (s[0] == '"')
            {
                int fin = s.IndexOf('"', 1);
                if (fin <= 1) return false;
                string ruta = s.Substring(1, fin - 1).Trim();
                string resuelta = ResolverArchivo(ruta);
                if (resuelta == null)
                {
                    problema = "No se encontró su desinstalador (" + ruta + "). Puede que el programa esté medio borrado: prueba desde Programas y características.";
                    return false;
                }
                exe = resuelta;
                args = s.Substring(fin + 1).Trim();
                return true;
            }

            // Sin comillas: el trozo más largo que acabe en ".exe" y exista
            string mejor = null;
            int finMejor = -1;
            int i = -1;
            while (true)
            {
                i = s.IndexOf(".exe", i + 1, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                string resuelta = ResolverArchivo(s.Substring(0, i + 4));
                if (resuelta != null)
                {
                    mejor = resuelta;
                    finMejor = i + 4;
                }
            }
            if (mejor != null)
            {
                exe = mejor;
                args = s.Substring(finMejor).Trim();
                return true;
            }

            // Por ejemplo "RunDll32 archivo.dll,Funcion": un programa de Windows sin extensión
            int espacio = s.IndexOf(' ');
            string primero = espacio > 0 ? s.Substring(0, espacio) : s;
            if (primero.IndexOf('.') < 0 && primero.IndexOf('\\') < 0)
            {
                string resuelta = ResolverArchivo(primero + ".exe");
                if (resuelta != null)
                {
                    exe = resuelta;
                    args = espacio > 0 ? s.Substring(espacio + 1).Trim() : "";
                    return true;
                }
            }
            return false;
        }

        // Ruta completa existente, o un programa de Windows (System32 / Windows) si viene sin carpeta
        private static string ResolverArchivo(string ruta)
        {
            try
            {
                if (string.IsNullOrEmpty(ruta)) return null;
                if (Path.IsPathRooted(ruta)) return File.Exists(ruta) ? ruta : null;
                if (ruta.IndexOf('\\') >= 0 || ruta.IndexOf('/') >= 0) return null;
                string sistema = Util.RutaSistema(ruta);
                if (File.Exists(sistema)) return sistema;
                string windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), ruta);
                if (File.Exists(windows)) return windows;
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------ estado vacío / cargando

        /// <summary>Mensaje centrado para cuando se está cargando, no hay nada o hubo un error.</summary>
        private class PanelEstado : TableLayoutPanel
        {
            private readonly Label icono, titulo, detalle;
            private readonly BarraUso barra;

            public PanelEstado()
            {
                Dock = DockStyle.Fill;
                Margin = new Padding(0);
                ColumnCount = 1;
                RowCount = 6;
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                for (int i = 0; i < 4; i++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
                RowStyles.Add(new RowStyle(SizeType.Percent, 50));

                icono = Tema.Icono(Tema.IcoInfo, 26f, Tema.TextoSuave);
                icono.Anchor = AnchorStyles.None;
                icono.Margin = new Padding(0, 0, 0, Tema.S(14));

                titulo = Tema.Etiqueta("", 12f, Tema.Texto, true);
                titulo.Anchor = AnchorStyles.None;
                titulo.TextAlign = ContentAlignment.MiddleCenter;
                titulo.MaximumSize = new Size(Tema.S(520), 0);

                detalle = Tema.Etiqueta("", 10f, Tema.TextoSuave);
                detalle.Anchor = AnchorStyles.None;
                detalle.TextAlign = ContentAlignment.MiddleCenter;
                detalle.MaximumSize = new Size(Tema.S(460), 0);
                detalle.Margin = new Padding(0, Tema.S(6), 0, 0);

                barra = new BarraUso();
                barra.Size = new Size(Tema.S(200), Tema.S(6));
                barra.Anchor = AnchorStyles.None;
                barra.Margin = new Padding(0, Tema.S(18), 0, 0);

                Controls.Add(icono, 0, 1);
                Controls.Add(titulo, 0, 2);
                Controls.Add(detalle, 0, 3);
                Controls.Add(barra, 0, 4);
            }

            public void Cargando(string texto)
            {
                icono.Visible = false;
                titulo.Text = texto;
                detalle.Visible = false;
                barra.Visible = true;
                barra.Indeterminado = true;
            }

            public void Mensaje(string glifo, Color color, string tituloTexto, string detalleTexto)
            {
                Detener();
                barra.Visible = false;
                icono.Text = glifo;
                icono.ForeColor = color;
                icono.Visible = true;
                titulo.Text = tituloTexto;
                detalle.Text = detalleTexto ?? "";
                detalle.Visible = !string.IsNullOrEmpty(detalleTexto);
            }

            public void Detener()
            {
                if (barra.Indeterminado) barra.Indeterminado = false;
            }
        }
    }
}
