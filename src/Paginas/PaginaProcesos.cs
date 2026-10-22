using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OptimizadorPC
{
    /// <summary>
    /// Programas abiertos ahora mismo, agrupados por nombre, con su memoria y su CPU.
    /// Se actualiza sola cada 3 segundos mientras está a la vista.
    /// </summary>
    class PaginaProcesos : PaginaBase
    {
        // ------------------------------------------------------------------ Windows

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long inactivo, out long nucleo, out long usuario);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int acceso, bool heredar, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr h, out long creacion, out long salida, out long nucleo, out long usuario);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int formato, StringBuilder ruta, ref int tamano);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
        private static extern int ExtractIconEx(string archivo, int indice, IntPtr[] grandes, IntPtr[] pequenos, int cantidad);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr h);

        // ------------------------------------------------------------------ datos

        /// <summary>Nombre amigable e icono de un programa (se calcula una vez por nombre).</summary>
        private class InfoPrograma
        {
            public string Visible;
            public string Archivo;     // "chrome.exe" (solo si Visible es la descripción del programa)
            public Bitmap Icono;
        }

        /// <summary>Todos los procesos con el mismo nombre.</summary>
        private class Grupo
        {
            public string Nombre;
            public string Visible;
            public string Archivo;
            public Bitmap Icono;
            public int Cantidad;
            public long Memoria;
            public long Ticks;
            public double Cpu = -1;
            public bool Protegido;
        }

        private class Muestra
        {
            public List<Grupo> Grupos = new List<Grupo>();
            public InfoMemoria Ram;
            public double CpuTotal = -1;
        }

        private struct TiempoPid
        {
            public long Creacion;
            public long Total;
        }

        private class ResultadoCierre
        {
            public int Total, Cerrados, Denegados, Otros, Omitidos;
        }

        /// <summary>Partes de Windows (y del anticheat Riot Vanguard) que no se dejan cerrar desde aquí.</summary>
        private static readonly string[] NombresProtegidos =
        {
            "System", "Idle", "Secure System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "dwm", "fontdrvhost", "explorer", "sihost", "ctfmon", "MsMpEng", "SecurityHealthService",
            "SecurityHealthSystray", "spoolsv", "audiodg", "Memory Compression", "RuntimeBroker", "StartMenuExperienceHost",
            "ShellExperienceHost", "SearchHost", "SearchIndexer", "TextInputHost", "conhost", "WmiPrvSE", "NisSrv", "LsaIso",
            "dllhost", "taskhostw", "smartscreen", "vgc", "vgtray"
        };

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
        private const string EstadoNormal = "Se actualiza cada 3 segundos";

        private readonly HashSet<string> protegidos = new HashSet<string>(NombresProtegidos, StringComparer.OrdinalIgnoreCase);
        private readonly int idPropio, sesionPropia;

        // Estado del muestreo: solo lo usa el hilo de trabajo y nunca hay dos muestreos a la vez
        private readonly Stopwatch reloj = Stopwatch.StartNew();
        private readonly Dictionary<string, InfoPrograma> infoProgramas = new Dictionary<string, InfoPrograma>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, TiempoPid> tiemposAnteriores = new Dictionary<int, TiempoPid>();
        private long marcaAnterior = -1, fileTimeAnterior;
        private long sisInactivo, sisNucleo, sisUsuario;
        private bool haySistemaAnterior;

        // Estado de la interfaz
        private readonly Timer temporizador;
        private bool refrescando, refrescoPendiente, cerrando, suprimirSeleccion, hayDatos;
        private List<Grupo> grupos = new List<Grupo>();
        private string seleccion;
        private int columnaOrden = 2;
        private bool descendente = true;
        private int idMensaje;
        private bool mensajeTemporal;

        // Controles
        private readonly TarjetaUso tarjetaRam, tarjetaCpu;
        private readonly string nombreCpu;
        private readonly Tarjeta tarjetaTabla;
        private readonly Panel cabeceraTabla;
        private readonly Label lblTablaTitulo, lblTablaCuenta, lblTablaEstado;
        private readonly DataGridView tabla;
        private readonly DataGridViewTextBoxColumn colPrograma, colCantidad, colMemoria, colCpu;
        private readonly Label lblCargando;
        private readonly Label lblPieIcono, lblPie;
        private readonly Boton btnIrInicio, btnCerrar;

        public PaginaProcesos() : base("Procesos", "Lo que está usando tu memoria ahora mismo")
        {
            using (Process yo = Process.GetCurrentProcess())
            {
                protegidos.Add(yo.ProcessName);
                idPropio = yo.Id;
                sesionPropia = yo.SessionId;
            }
            nombreCpu = NombreProcesador();

            // ---- Encabezado ----
            btnCerrar = new Boton("Cerrar programa", EstiloBoton.Peligro, Tema.IcoCancelar);
            btnCerrar.Enabled = false;
            btnCerrar.Click += delegate { CerrarSeleccionado(); };
            Acciones.Controls.Add(btnCerrar);

            // ---- Tarjetas de RAM y procesador ----
            tarjetaRam = new TarjetaUso("Memoria RAM");
            tarjetaCpu = new TarjetaUso("Procesador");

            // ---- Tabla ----
            tarjetaTabla = new Tarjeta();
            tarjetaTabla.Padding = Tema.S(16, 6, 16, 10);

            cabeceraTabla = new Panel();
            cabeceraTabla.Dock = DockStyle.Top;
            cabeceraTabla.Height = Tema.S(46);
            lblTablaTitulo = Tema.Etiqueta("Programas abiertos", 11f, Tema.Texto, true);
            lblTablaCuenta = Tema.Etiqueta("", 9.5f, Tema.TextoSuave);
            lblTablaEstado = Tema.Etiqueta(EstadoNormal, 9f, Tema.TextoTenue);
            lblTablaEstado.AutoSize = false;
            lblTablaEstado.AutoEllipsis = true;
            lblTablaEstado.TextAlign = ContentAlignment.MiddleRight;
            cabeceraTabla.Controls.Add(lblTablaTitulo);
            cabeceraTabla.Controls.Add(lblTablaCuenta);
            cabeceraTabla.Controls.Add(lblTablaEstado);
            cabeceraTabla.Resize += delegate { AcomodarCabecera(); };
            lblTablaCuenta.SizeChanged += delegate { AcomodarCabecera(); };

            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.ReadOnly = true;
            tabla.ShowCellToolTips = true;

            colPrograma = Columna("Programa", 52, false);
            colPrograma.MinimumWidth = Tema.S(180);
            colCantidad = Columna("Procesos", 14, true);
            colCantidad.MinimumWidth = Tema.S(90);
            colMemoria = Columna("Memoria", 18, true);
            colMemoria.MinimumWidth = Tema.S(100);
            colCpu = Columna("CPU", 14, true);
            colCpu.MinimumWidth = Tema.S(80);
            tabla.Columns.AddRange(new DataGridViewColumn[] { colPrograma, colCantidad, colMemoria, colCpu });

            tabla.CellPainting += Tabla_CellPainting;
            tabla.SelectionChanged += Tabla_SelectionChanged;
            tabla.ColumnHeaderMouseClick += Tabla_ColumnHeaderMouseClick;
            tabla.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete && btnCerrar.Enabled) { e.Handled = true; CerrarSeleccionado(); }
            };
            ActualizarGlifos();

            lblCargando = Tema.Etiqueta("Cargando…", 10.5f, Tema.TextoSuave);
            lblCargando.AutoSize = false;
            lblCargando.Dock = DockStyle.Fill;
            lblCargando.TextAlign = ContentAlignment.MiddleCenter;

            tarjetaTabla.Controls.Add(tabla);
            tarjetaTabla.Controls.Add(lblCargando);
            tarjetaTabla.Controls.Add(cabeceraTabla);
            tabla.BringToFront();
            lblCargando.BringToFront();
            tabla.Visible = false;

            // ---- Pie: pista para ir a Inicio de Windows ----
            lblPieIcono = Tema.Icono(Tema.IcoInfo, 10f, Tema.TextoTenue);
            lblPie = Tema.Etiqueta("Cerrar un programa solo lo detiene ahora; para que no se abra al encender, ve a Inicio de Windows.",
                9f, Tema.TextoTenue);
            lblPie.AutoSize = false;
            btnIrInicio = new Boton("Ir a Inicio de Windows", EstiloBoton.Fantasma, Tema.IcoInicio);
            btnIrInicio.Margin = new Padding(0);
            btnIrInicio.Click += delegate { IrA("inicio"); };

            Contenido.Controls.Add(tarjetaRam);
            Contenido.Controls.Add(tarjetaCpu);
            Contenido.Controls.Add(tarjetaTabla);
            Contenido.Controls.Add(lblPieIcono);
            Contenido.Controls.Add(lblPie);
            Contenido.Controls.Add(btnIrInicio);
            Contenido.Resize += delegate { Acomodar(); };

            // ---- Actualización automática (solo mientras la página se ve) ----
            temporizador = new Timer();
            temporizador.Interval = 3000;
            temporizador.Tick += delegate
            {
                if (temporizador.Interval != 3000) temporizador.Interval = 3000;
                Form f = FindForm();
                if (f != null && f.WindowState == FormWindowState.Minimized) return;
                Refrescar(false);
            };
            VisibleChanged += delegate { if (!Visible) temporizador.Stop(); };

            Acomodar();
        }

        private static DataGridViewTextBoxColumn Columna(string titulo, int peso, bool numerica)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = titulo;
            c.ReadOnly = true;
            c.FillWeight = peso;
            c.SortMode = DataGridViewColumnSortMode.Programmatic;
            if (numerica)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            return c;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                temporizador.Stop();
                temporizador.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ diseño

        private void Acomodar()
        {
            int w = Contenido.ClientSize.Width, h = Contenido.ClientSize.Height;
            if (w <= 0) return;
            int hueco = Tema.S(16);

            // Fila de tarjetas (mitad y mitad)
            int altoUso = Tema.S(112);
            int mitad = (w - hueco) / 2;
            tarjetaRam.SetBounds(0, 0, mitad, altoUso);
            tarjetaCpu.SetBounds(mitad + hueco, 0, w - mitad - hueco, altoUso);

            // Pie: icono + texto (se parte en dos líneas si no cabe) + botón
            int xTexto = Tema.S(4) + lblPieIcono.Width + Tema.S(10);
            int anchoTexto = Math.Max(Tema.S(120), w - xTexto - btnIrInicio.Width - Tema.S(12));
            Size st = lblPie.GetPreferredSize(new Size(anchoTexto, 0));
            int anchoReal = Math.Min(anchoTexto, st.Width + Tema.S(2));
            int altoPie = Math.Max(st.Height, btnIrInicio.Height);
            int yPie = h - altoPie;
            lblPie.SetBounds(xTexto, yPie + (altoPie - st.Height) / 2, anchoReal, st.Height);
            lblPieIcono.Location = new Point(Tema.S(4), lblPie.Top + Tema.S(1));
            btnIrInicio.Location = new Point(lblPie.Right + Tema.S(8), yPie + (altoPie - btnIrInicio.Height) / 2);

            int y = altoUso + hueco;
            tarjetaTabla.SetBounds(0, y, w, Math.Max(Tema.S(160), yPie - Tema.S(12) - y));
        }

        private void AcomodarCabecera()
        {
            int w = cabeceraTabla.ClientSize.Width, h = cabeceraTabla.ClientSize.Height;
            int x = Tema.S(10);
            lblTablaTitulo.Location = new Point(x, (h - lblTablaTitulo.Height) / 2);
            lblTablaCuenta.Location = new Point(lblTablaTitulo.Right + Tema.S(10),
                lblTablaTitulo.Top + (lblTablaTitulo.Height - lblTablaCuenta.Height) / 2 + Tema.S(1));
            int xEstado = lblTablaCuenta.Right + Tema.S(16);
            lblTablaEstado.SetBounds(xEstado, 0, Math.Max(0, w - xEstado - Tema.S(10)), h);
        }

        /// <summary>Tarjeta con título, porcentaje grande, detalle y barra.</summary>
        private class TarjetaUso : Tarjeta
        {
            public readonly Label Titulo, Valor, Detalle;
            public readonly BarraUso Barra;

            public TarjetaUso(string titulo)
            {
                Padding = new Padding(Tema.S(20), Tema.S(18), Tema.S(20), Tema.S(20));
                Titulo = Tema.Etiqueta(titulo, 10.5f, Tema.Texto, true);
                Valor = Tema.Etiqueta("—", 18f, Tema.Texto, true);
                Detalle = Tema.Etiqueta("Midiendo…", 9.5f, Tema.TextoSuave);
                Detalle.AutoSize = false;
                Detalle.AutoEllipsis = true;
                Barra = new BarraUso();
                Controls.Add(Titulo);
                Controls.Add(Valor);
                Controls.Add(Detalle);
                Controls.Add(Barra);
                Valor.SizeChanged += delegate { Colocar(); };
                Colocar();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                Colocar();
            }

            private void Colocar()
            {
                if (Titulo == null || Barra == null) return;
                int x = Padding.Left, derecha = Width - Padding.Right;
                Titulo.Location = new Point(x, Padding.Top);
                Valor.Location = new Point(Math.Max(x, derecha - Valor.Width + Tema.S(2)), Padding.Top + (Titulo.Height - Valor.Height) / 2);
                Detalle.SetBounds(x, Titulo.Bottom + Tema.S(8), Math.Max(0, derecha - x), Detalle.PreferredHeight);
                Barra.SetBounds(x, Height - Padding.Bottom - Barra.Height, Math.Max(0, derecha - x), Barra.Height);
            }

            public void Poner(double porcentaje, string detalle)
            {
                if (porcentaje < 0)
                {
                    Valor.Text = "—";
                    Valor.ForeColor = Tema.TextoSuave;
                    Barra.Valor = 0;
                }
                else
                {
                    Color c = Tema.ColorSegunUso(porcentaje);
                    Valor.Text = Math.Round(porcentaje).ToString("0", Es) + " %";
                    Valor.ForeColor = c;
                    Barra.ColorRelleno = c;
                    Barra.Valor = porcentaje;
                }
                Detalle.Text = detalle;
            }
        }

        // ------------------------------------------------------------------ actualización

        public override void AlMostrar()
        {
            if (temporizador.Enabled) return;
            // La primera medida de CPU necesita dos muestras: la siguiente llega en 1 segundo
            temporizador.Interval = 1000;
            temporizador.Start();
            Refrescar(true);
        }

        private async void Refrescar(bool forzar)
        {
            if (refrescando)
            {
                if (forzar) refrescoPendiente = true;
                return;
            }
            refrescando = true;
            try
            {
                Muestra m = await Task.Run(() => Muestrear());
                if (IsDisposed) return;
                Aplicar(m);
            }
            catch (Exception ex)
            {
                Util.Log("Procesos: error al actualizar: " + ex);
            }
            finally
            {
                refrescando = false;
            }
            if (refrescoPendiente && !IsDisposed)
            {
                refrescoPendiente = false;
                Refrescar(false);
            }
        }

        /// <summary>Lee todos los procesos (en segundo plano). Es solo lectura.</summary>
        private Muestra Muestrear()
        {
            Muestra m = new Muestra();
            m.Ram = Sistema.Memoria();

            long inactivo, nucleo, usuario;
            if (GetSystemTimes(out inactivo, out nucleo, out usuario))
            {
                if (haySistemaAnterior)
                {
                    // El tiempo de núcleo ya incluye el tiempo inactivo
                    long di = inactivo - sisInactivo, total = (nucleo - sisNucleo) + (usuario - sisUsuario);
                    if (total > 0) m.CpuTotal = Math.Max(0, Math.Min(100, (total - di) * 100.0 / total));
                }
                sisInactivo = inactivo;
                sisNucleo = nucleo;
                sisUsuario = usuario;
                haySistemaAnterior = true;
            }

            long marca = reloj.Elapsed.Ticks;
            long ahoraFt = DateTime.UtcNow.ToFileTimeUtc();
            bool hayAnterior = marcaAnterior >= 0;
            double transcurrido = hayAnterior ? marca - marcaAnterior : 0;

            Dictionary<string, Grupo> porNombre = new Dictionary<string, Grupo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, TiempoPid> nuevos = new Dictionary<int, TiempoPid>();
            Process[] lista;
            try { lista = Process.GetProcesses(); }
            catch (Exception ex) { Util.Log("Procesos: no se pudo listar: " + ex.Message); lista = new Process[0]; }

            foreach (Process p in lista)
            {
                try
                {
                    int pid = p.Id;
                    if (pid == 0) continue;   // "Idle": es el tiempo libre del procesador, no un programa
                    string nombre = p.ProcessName;
                    Grupo g;
                    if (!porNombre.TryGetValue(nombre, out g))
                    {
                        g = new Grupo();
                        g.Nombre = nombre;
                        g.Protegido = protegidos.Contains(nombre);
                        porNombre[nombre] = g;
                    }
                    g.Cantidad++;
                    try { g.Memoria += p.WorkingSet64; } catch { }

                    IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) continue;   // sin permiso: su CPU cuenta como 0
                    try
                    {
                        long creacion, salida, kn, us;
                        if (GetProcessTimes(h, out creacion, out salida, out kn, out us))
                        {
                            long total = kn + us, delta = 0;
                            TiempoPid t;
                            if (tiemposAnteriores.TryGetValue(pid, out t) && t.Creacion == creacion) delta = total - t.Total;
                            else if (hayAnterior && creacion > fileTimeAnterior) delta = total;   // empezó después de la última muestra
                            if (delta > 0) g.Ticks += delta;
                            TiempoPid n;
                            n.Creacion = creacion;
                            n.Total = total;
                            nuevos[pid] = n;
                        }
                        if (!infoProgramas.ContainsKey(nombre))
                        {
                            InfoPrograma info = LeerInfo(h, nombre);
                            if (info != null) infoProgramas[nombre] = info;
                        }
                    }
                    finally
                    {
                        CloseHandle(h);
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            tiemposAnteriores = nuevos;
            marcaAnterior = marca;
            fileTimeAnterior = ahoraFt;

            int nucleos = Math.Max(1, Environment.ProcessorCount);
            foreach (Grupo g in porNombre.Values)
            {
                InfoPrograma info;
                if (!infoProgramas.TryGetValue(g.Nombre, out info))
                {
                    // No se pudo leer su archivo: se guarda igualmente para no reintentarlo cada vez
                    info = new InfoPrograma();
                    info.Visible = NombreAmigable(g.Nombre);
                    infoProgramas[g.Nombre] = info;
                }
                g.Visible = info.Visible;
                g.Archivo = info.Archivo;
                g.Icono = info.Icono;
                g.Cpu = hayAnterior && transcurrido > 0 ? Math.Min(100, g.Ticks * 100.0 / (transcurrido * nucleos)) : -1;
                m.Grupos.Add(g);
            }
            return m;
        }

        private static InfoPrograma LeerInfo(IntPtr h, string nombre)
        {
            StringBuilder sb = new StringBuilder(1024);
            int tam = sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref tam) || tam <= 0) return null;
            string ruta = sb.ToString();

            InfoPrograma info = new InfoPrograma();
            string archivo = "";
            try { archivo = Path.GetFileName(ruta); } catch { }
            string desc = "";
            try { desc = (FileVersionInfo.GetVersionInfo(ruta).FileDescription ?? "").Trim(); } catch { }

            bool sirve = desc.Length > 1 &&
                !string.Equals(desc, nombre, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(desc, archivo, StringComparison.OrdinalIgnoreCase);
            if (sirve)
            {
                info.Visible = desc;
                info.Archivo = archivo;
            }
            else
            {
                info.Visible = NombreAmigable(nombre);
            }
            info.Icono = ExtraerIcono(ruta);
            return info;
        }

        private static string NombreAmigable(string nombre)
        {
            switch ((nombre ?? "").ToLowerInvariant())
            {
                case "system": return "Sistema de Windows";
                case "secure system": return "Sistema seguro de Windows";
                case "registry": return "Registro de Windows";
                case "memory compression": return "Compresión de memoria";
            }
            return nombre;
        }

        private static Bitmap ExtraerIcono(string ruta)
        {
            IntPtr[] pequenos = new IntPtr[1];
            try
            {
                if (ExtractIconEx(ruta, 0, null, pequenos, 1) > 0 && pequenos[0] != IntPtr.Zero)
                {
                    using (Icon ic = Icon.FromHandle(pequenos[0]))
                    {
                        return ic.ToBitmap();
                    }
                }
            }
            catch { }
            finally
            {
                if (pequenos[0] != IntPtr.Zero) DestroyIcon(pequenos[0]);
            }
            return null;
        }

        private static string NombreProcesador()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (k == null) return null;
                    string n = Convert.ToString(k.GetValue("ProcessorNameString")) ?? "";
                    n = Regex.Replace(n, @"\((R|TM|tm|r)\)", "");
                    n = Regex.Replace(n, @"\s+CPU\s*@.*$", "");
                    n = Regex.Replace(n, @"\s+(Processor|CPU)\s*$", "", RegexOptions.IgnoreCase);
                    n = Regex.Replace(n, @"\s+\d+-Core\s*$", "", RegexOptions.IgnoreCase);
                    n = Regex.Replace(n, @"\s+", " ").Trim();
                    return n.Length > 0 ? n : null;
                }
            }
            catch { return null; }
        }

        /// <summary>Pinta una muestra nueva en las tarjetas y en la tabla.</summary>
        private void Aplicar(Muestra m)
        {
            InfoMemoria ram = m.Ram;
            if (ram != null && ram.Total > 0)
            {
                tarjetaRam.Poner(ram.Porcentaje, Gb(ram.Usada) + " de " + Gb(ram.Total) + " en uso");
            }
            tarjetaCpu.Poner(m.CpuTotal, m.CpuTotal < 0 ? "Midiendo…"
                : "Uso total ahora mismo" + (nombreCpu != null ? " · " + nombreCpu : ""));

            grupos = m.Grupos;
            int procesos = grupos.Sum(g => g.Cantidad);
            lblTablaCuenta.Text = grupos.Count + (grupos.Count == 1 ? " programa" : " programas") + " · " +
                procesos + (procesos == 1 ? " proceso" : " procesos");

            if (!hayDatos)
            {
                hayDatos = true;
                lblCargando.Visible = false;
                tabla.Visible = true;
            }
            MostrarGrupos();
        }

        private static string Gb(long bytes)
        {
            return (bytes / (1024.0 * 1024 * 1024)).ToString("N1", Es) + " GB";
        }

        private static string FormatoMemoria(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1000) return (mb / 1024.0).ToString("N1", Es) + " GB";
            if (mb >= 10) return mb.ToString("N0", Es) + " MB";
            if (mb >= 0.1) return mb.ToString("N1", Es) + " MB";
            return (bytes / 1024.0).ToString("N0", Es) + " KB";
        }

        private static string FormatoCpu(double v)
        {
            if (v < 0) return "—";
            if (v < 0.05) return "0 %";
            return v.ToString("0.0", Es) + " %";
        }

        private int Comparar(Grupo a, Grupo b)
        {
            int r;
            switch (columnaOrden)
            {
                case 0: r = string.Compare(a.Visible, b.Visible, StringComparison.CurrentCultureIgnoreCase); break;
                case 1: r = a.Cantidad.CompareTo(b.Cantidad); break;
                case 3: r = a.Cpu.CompareTo(b.Cpu); break;
                default: r = a.Memoria.CompareTo(b.Memoria); break;
            }
            if (descendente) r = -r;
            if (r == 0) r = -a.Memoria.CompareTo(b.Memoria);
            if (r == 0) r = string.Compare(a.Nombre, b.Nombre, StringComparison.OrdinalIgnoreCase);
            return r;
        }

        /// <summary>
        /// Ordena y escribe los grupos en la tabla. Las filas se reutilizan (solo cambia lo que cambió),
        /// así no parpadea y se conservan la selección (por nombre) y el scroll.
        /// </summary>
        private void MostrarGrupos()
        {
            List<Grupo> l = new List<Grupo>(grupos);
            l.Sort(Comparar);
            grupos = l;

            int primera = tabla.Rows.Count > 0 ? tabla.FirstDisplayedScrollingRowIndex : -1;
            suprimirSeleccion = true;
            try
            {
                if (tabla.Rows.Count < l.Count) tabla.Rows.Add(l.Count - tabla.Rows.Count);
                while (tabla.Rows.Count > l.Count) tabla.Rows.RemoveAt(tabla.Rows.Count - 1);
                for (int i = 0; i < l.Count; i++) PonerFila(tabla.Rows[i], l[i]);

                int idx = -1;
                if (seleccion != null)
                {
                    idx = l.FindIndex(g => string.Equals(g.Nombre, seleccion, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) seleccion = null;   // el programa ya se cerró
                }
                if (idx < 0)
                {
                    if (tabla.SelectedRows.Count > 0) tabla.ClearSelection();
                    if (tabla.CurrentCell != null) tabla.CurrentCell = null;
                }
                else
                {
                    foreach (DataGridViewRow f in tabla.SelectedRows)
                    {
                        if (f.Index != idx) f.Selected = false;
                    }
                    DataGridViewRow fila = tabla.Rows[idx];
                    if (!fila.Selected) fila.Selected = true;
                    // La celda actual sigue a la selección solo si ya está a la vista (para no mover el scroll)
                    if ((tabla.CurrentCell == null || tabla.CurrentCell.RowIndex != idx) && fila.Displayed)
                    {
                        try { tabla.CurrentCell = fila.Cells[colPrograma.Index]; } catch { }
                    }
                }

                if (primera >= 0 && primera < tabla.Rows.Count && tabla.FirstDisplayedScrollingRowIndex != primera)
                {
                    try { tabla.FirstDisplayedScrollingRowIndex = primera; } catch { }
                }
            }
            finally
            {
                suprimirSeleccion = false;
            }
            ActualizarBotonCerrar();
        }

        private static void Poner(DataGridViewCell c, string valor)
        {
            if (!string.Equals(c.Value as string, valor, StringComparison.Ordinal)) c.Value = valor;
        }

        private void PonerFila(DataGridViewRow f, Grupo g)
        {
            Grupo anterior = f.Tag as Grupo;
            f.Tag = g;
            Poner(f.Cells[colPrograma.Index], g.Visible);
            Poner(f.Cells[colCantidad.Index], g.Cantidad.ToString(Es));
            Poner(f.Cells[colMemoria.Index], FormatoMemoria(g.Memoria));
            Poner(f.Cells[colCpu.Index], FormatoCpu(g.Cpu));

            if (anterior == null || anterior.Protegido != g.Protegido)
            {
                f.DefaultCellStyle.ForeColor = g.Protegido ? Tema.TextoTenue : Tema.Texto;
                f.DefaultCellStyle.SelectionForeColor = g.Protegido ? Tema.TextoSuave : Tema.Texto;
            }
            string ayuda = g.Protegido ? "Protegido: es parte de Windows (o del anticheat) y no se cierra desde aquí"
                : (g.Archivo ?? g.Nombre);
            if (f.Cells[colPrograma.Index].ToolTipText != ayuda) f.Cells[colPrograma.Index].ToolTipText = ayuda;
            // El icono y el nombre de archivo se pintan a mano: si cambió el programa de esta fila, se repinta
            if (anterior == null || !ReferenceEquals(anterior.Icono, g.Icono) || anterior.Archivo != g.Archivo)
            {
                tabla.InvalidateCell(colPrograma.Index, f.Index);
            }
        }

        // Encabezados: el texto queda alineado con los valores y la flecha de orden va al lado del texto
        private void PintarEncabezado(DataGridViewCellPaintingEventArgs e)
        {
            e.PaintBackground(e.CellBounds, false);
            Graphics gr = e.Graphics;
            Rectangle r = e.CellBounds;
            DataGridViewColumn col = tabla.Columns[e.ColumnIndex];
            bool ordenada = e.ColumnIndex == columnaOrden;
            bool derecha = col != colPrograma;
            int margen = Tema.S(10);
            Font f = e.CellStyle.Font;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            string texto = col.HeaderText;
            Size st = TextRenderer.MeasureText(gr, texto, f, new Size(int.MaxValue, r.Height), flags);
            int xTexto = derecha ? r.Right - margen - st.Width : r.X + margen;
            TextRenderer.DrawText(gr, texto, f, new Rectangle(xTexto, r.Y, st.Width + Tema.S(2), r.Height),
                ordenada ? Tema.Texto : Tema.TextoSuave, flags);

            if (ordenada)
            {
                string flecha = descendente ? "" : "";
                Font fi = Tema.FuenteIconos(7f);
                Size sf = TextRenderer.MeasureText(gr, flecha, fi, new Size(int.MaxValue, r.Height), flags);
                int xf = derecha ? xTexto - Tema.S(6) - sf.Width : xTexto + st.Width + Tema.S(6);
                TextRenderer.DrawText(gr, flecha, fi, new Rectangle(xf, r.Y + Tema.S(1), sf.Width + Tema.S(2), r.Height), Tema.Acento, flags);
            }
            e.Handled = true;
        }

        // Columna "Programa": icono + nombre + archivo en gris
        private void Tabla_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                PintarEncabezado(e);
                return;
            }
            if (e.RowIndex < 0 || e.ColumnIndex != colPrograma.Index) return;
            Grupo g = tabla.Rows[e.RowIndex].Tag as Grupo;
            if (g == null) return;

            bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, sel);
            Graphics gr = e.Graphics;
            Rectangle r = e.CellBounds;

            int lado = Tema.S(16);
            Rectangle ri = new Rectangle(r.X + Tema.S(10), r.Y + (r.Height - lado) / 2, lado, lado);
            if (g.Icono != null)
            {
                InterpolationMode modo = gr.InterpolationMode;
                gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (g.Protegido)
                {
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ColorMatrix cm = new ColorMatrix();
                        cm.Matrix33 = 0.45f;
                        ia.SetColorMatrix(cm);
                        gr.DrawImage(g.Icono, ri, 0, 0, g.Icono.Width, g.Icono.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                else
                {
                    gr.DrawImage(g.Icono, ri);
                }
                gr.InterpolationMode = modo;
            }
            else
            {
                // Sin icono: cuadradito con la inicial
                SmoothingMode suave = gr.SmoothingMode;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = Tema.Redondeado(new RectangleF(ri.X, ri.Y, ri.Width - 1, ri.Height - 1), Tema.S(4)))
                using (SolidBrush b = new SolidBrush(Tema.Control))
                {
                    gr.FillPath(b, p);
                }
                gr.SmoothingMode = suave;
                string inicial = string.IsNullOrEmpty(g.Visible) ? "?" : g.Visible.Substring(0, 1).ToUpperInvariant();
                TextRenderer.DrawText(gr, inicial, Tema.FuenteSemibold(7f), ri, Tema.TextoSuave,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            int x = ri.Right + Tema.S(10);
            int disponible = r.Right - x - Tema.S(8);
            if (disponible > 0)
            {
                Color c = sel ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
                Font f = e.CellStyle.Font;
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                string principal = g.Visible ?? "";
                Size sp = TextRenderer.MeasureText(gr, principal, f, new Size(int.MaxValue, r.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(gr, principal, f, new Rectangle(x, r.Y, disponible, r.Height), c, flags);

                int xs = x + sp.Width + Tema.S(8);
                if (!string.IsNullOrEmpty(g.Archivo) && r.Right - Tema.S(8) - xs > Tema.S(40))
                {
                    TextRenderer.DrawText(gr, g.Archivo, Tema.Fuente(8.5f), new Rectangle(xs, r.Y + Tema.S(1), r.Right - Tema.S(8) - xs, r.Height),
                        sel ? Tema.TextoSuave : Tema.TextoTenue, flags);
                }
            }
            e.Handled = true;
        }

        private void Tabla_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            if (e.ColumnIndex == columnaOrden) descendente = !descendente;
            else
            {
                columnaOrden = e.ColumnIndex;
                descendente = e.ColumnIndex != colPrograma.Index;   // números: de mayor a menor
            }
            ActualizarGlifos();
            if (hayDatos) MostrarGrupos();
        }

        private void ActualizarGlifos()
        {
            foreach (DataGridViewColumn c in tabla.Columns)
            {
                c.HeaderCell.SortGlyphDirection = c.Index == columnaOrden
                    ? (descendente ? SortOrder.Descending : SortOrder.Ascending)
                    : SortOrder.None;
            }
        }

        // ------------------------------------------------------------------ selección y cierre

        private void Tabla_SelectionChanged(object sender, EventArgs e)
        {
            if (suprimirSeleccion) return;
            Grupo g = tabla.SelectedRows.Count > 0 ? tabla.SelectedRows[0].Tag as Grupo : null;
            seleccion = g != null ? g.Nombre : null;
            ActualizarBotonCerrar();
        }

        private Grupo GrupoSeleccionado()
        {
            if (seleccion == null) return null;
            foreach (Grupo g in grupos)
            {
                if (string.Equals(g.Nombre, seleccion, StringComparison.OrdinalIgnoreCase)) return g;
            }
            return null;
        }

        private void ActualizarBotonCerrar()
        {
            Grupo g = GrupoSeleccionado();
            btnCerrar.Enabled = !cerrando && g != null && !g.Protegido;
            if (mensajeTemporal) return;
            if (g != null && g.Protegido)
            {
                lblTablaEstado.Text = "«" + g.Visible + "» está protegido: no se cierra desde aquí";
                lblTablaEstado.ForeColor = Tema.TextoSuave;
            }
            else
            {
                lblTablaEstado.Text = EstadoNormal;
                lblTablaEstado.ForeColor = Tema.TextoTenue;
            }
        }

        private async void MostrarMensaje(string texto, Color color)
        {
            int id = ++idMensaje;
            mensajeTemporal = true;
            lblTablaEstado.Text = texto;
            lblTablaEstado.ForeColor = color;
            await Task.Delay(5000);
            if (IsDisposed || id != idMensaje) return;
            mensajeTemporal = false;
            ActualizarBotonCerrar();
        }

        private async void CerrarSeleccionado()
        {
            if (cerrando) return;
            Grupo g = GrupoSeleccionado();
            if (g == null || g.Protegido || protegidos.Contains(g.Nombre)) return;
            string nombre = g.Nombre, visible = g.Visible ?? g.Nombre;

            cerrando = true;
            btnCerrar.Enabled = false;
            try
            {
                // Se cuentan los procesos justo ahora, no los de la última actualización
                List<int> pids = await Task.Run(() => PidsDe(nombre));
                if (IsDisposed) return;
                if (pids.Count == 0)
                {
                    Dialogo.Info(FindForm(), "Ya está cerrado", "«" + visible + "» ya no está abierto.");
                    return;
                }
                string cuantos = pids.Count == 1 ? "1 proceso" : pids.Count + " procesos";
                if (!Dialogo.Confirmar(FindForm(), "¿Cerrar «" + visible + "»?",
                    "Se cerrará " + visible + " (" + cuantos + "). Lo que no hayas guardado se perderá.",
                    "Cerrar programa", true))
                {
                    return;
                }
                if (IsDisposed) return;

                ResultadoCierre r = await Task.Run(() => Cerrar(nombre, pids));
                if (IsDisposed) return;
                int total = r.Total - r.Omitidos;
                int fallidos = r.Denegados + r.Otros;
                Util.Log(string.Format("Procesos: cerrar '{0}' -> {1} de {2} procesos cerrados ({3} sin permiso, {4} con error)",
                    nombre, r.Cerrados, total, r.Denegados, r.Otros));

                if (fallidos == 0)
                {
                    MostrarMensaje("Se cerró «" + visible + "»", Tema.Exito);
                }
                else
                {
                    string motivo = r.Denegados > 0 && !Util.EsAdmin
                        ? "Hacen falta permisos de administrador. Abre el Optimizador como administrador e inténtalo otra vez."
                        : "Windows no dejó cerrarlo. Puede que sea un programa protegido o que se esté cerrando ya.";
                    if (r.Cerrados == 0)
                    {
                        Dialogo.Error(FindForm(), "No se pudo cerrar «" + visible + "»", motivo);
                    }
                    else
                    {
                        Dialogo.Error(FindForm(), "No se pudo cerrar del todo",
                            "Se cerraron " + r.Cerrados + " de " + total + " procesos de " + visible + ". " + motivo);
                    }
                }
            }
            catch (Exception ex)
            {
                Util.Log("Procesos: error al cerrar '" + nombre + "': " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "No se pudo cerrar «" + visible + "»", "Algo salió mal: " + ex.Message);
            }
            finally
            {
                cerrando = false;
                if (!IsDisposed)
                {
                    ActualizarBotonCerrar();
                    Refrescar(true);
                }
            }
        }

        /// <summary>Procesos con ese nombre en tu sesión (o servicios de la sesión 0). Nunca este mismo programa.</summary>
        private List<int> PidsDe(string nombre)
        {
            List<int> l = new List<int>();
            Process[] ps;
            try { ps = Process.GetProcessesByName(nombre); }
            catch { return l; }
            foreach (Process p in ps)
            {
                try
                {
                    if (p.Id != idPropio && (p.SessionId == sesionPropia || p.SessionId == 0)) l.Add(p.Id);
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
            return l;
        }

        /// <summary>Cierra esos procesos uno a uno, comprobando antes que siguen siendo el mismo programa.</summary>
        private ResultadoCierre Cerrar(string nombre, List<int> pids)
        {
            ResultadoCierre r = new ResultadoCierre();
            r.Total = pids.Count;
            foreach (int pid in pids)
            {
                Process p;
                try { p = Process.GetProcessById(pid); }
                catch (ArgumentException) { r.Cerrados++; continue; }   // ya no existe
                catch { r.Otros++; continue; }

                using (p)
                {
                    try
                    {
                        string actual = p.ProcessName;
                        if (pid == idPropio || !string.Equals(actual, nombre, StringComparison.OrdinalIgnoreCase) || protegidos.Contains(actual))
                        {
                            r.Omitidos++;
                            continue;
                        }
                        p.Kill();
                        try { p.WaitForExit(3000); } catch { }
                        r.Cerrados++;
                    }
                    catch (Win32Exception) { r.Denegados++; }
                    catch (InvalidOperationException) { r.Cerrados++; }   // terminó justo antes
                    catch (Exception ex)
                    {
                        r.Otros++;
                        Util.Log("Procesos: no se pudo cerrar el proceso " + pid + ": " + ex.Message);
                    }
                }
            }
            return r;
        }
    }
}
