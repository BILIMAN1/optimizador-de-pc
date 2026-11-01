using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>
    /// Apps de Microsoft Store que vienen preinstaladas y casi nadie usa. Se quitan solo
    /// del usuario actual y se pueden volver a instalar gratis desde la Store.
    /// </summary>
    class PaginaApps : PaginaBase
    {
        private const string IcoPapelera = "";

        private class AppConocida
        {
            public string Patron, Nombre, Descripcion;
            /// <summary>Si es true sale marcada de entrada; las "solo si no la usas" no.</summary>
            public bool Recomendada;
        }

        private class AppInstalada
        {
            public AppConocida Info;
            public string Paquete;   // nombre del paquete sin versión (p. ej. Microsoft.BingNews)
            public string Nombre;    // nombre que entiende cualquiera
            public string Familia;   // PackageFamilyName: su carpeta de datos es %LOCALAPPDATA%\Packages\<Familia>
            public bool Conocida;    // está en la lista de apps basura
            public readonly List<string> Completos = new List<string>();   // PackageFullName (puede haber varias versiones)
        }

        private static AppConocida A(string patron, string nombre, string descripcion, bool recomendada)
        {
            AppConocida a = new AppConocida();
            a.Patron = patron;
            a.Nombre = nombre;
            a.Descripcion = descripcion;
            a.Recomendada = recomendada;
            return a;
        }

        // Orden: de lo más concreto a lo más general (*Instagram* antes que *Facebook*)
        private static readonly AppConocida[] Conocidas =
        {
            A("Microsoft.BingNews", "Noticias (MSN)", "Titulares y noticias de MSN", true),
            A("Microsoft.BingWeather", "El Tiempo (MSN)", "Previsión del tiempo de MSN", true),
            A("Microsoft.BingSearch", "Búsqueda de Bing", "Búsquedas web de Bing desde Windows", true),
            A("Microsoft.GetHelp", "Obtener ayuda", "Asistente de soporte técnico de Microsoft", true),
            A("Microsoft.Getstarted", "Consejos", "Trucos y presentación de Windows", true),
            A("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", "Solitario y otros juegos de cartas, con anuncios", true),
            A("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (anuncio de Office)", "Acceso que anuncia Office y Microsoft 365", true),
            A("Microsoft.People", "Contactos", "Agenda de contactos antigua de Windows", true),
            A("Microsoft.WindowsFeedbackHub", "Centro de opiniones", "Sirve para enviar comentarios a Microsoft", true),
            A("Microsoft.WindowsMaps", "Mapas", "App de mapas de Windows", true),
            A("Microsoft.ZuneVideo", "Películas y TV", "Tienda y reproductor de vídeos de Microsoft", true),
            A("Microsoft.ZuneMusic", "Reproductor multimedia", "Reproductor de música y vídeo (solo si no lo usas)", false),
            A("Microsoft.549981C3F5F10", "Cortana", "Asistente de voz que Microsoft ya retiró", true),
            A("Microsoft.Todos", "Microsoft To Do", "Lista de tareas (solo si no la usas)", false),
            A("Microsoft.PowerAutomateDesktop", "Power Automate", "Automatiza tareas; casi nadie lo usa en casa", true),
            A("Microsoft.MixedReality.Portal", "Portal de realidad mixta", "Solo sirve con gafas de realidad virtual", true),
            A("Microsoft.SkypeApp", "Skype", "Llamadas por internet; Microsoft ya lo cerró", true),
            A("Microsoft.WindowsSoundRecorder", "Grabadora de sonido", "Graba audio con el micrófono (solo si no la usas)", false),
            A("Microsoft.Microsoft3DViewer", "Visor 3D", "Visor de modelos en 3D", true),
            A("Microsoft.MSPaint", "Paint 3D", "Editor 3D ya retirado (no es el Paint de siempre)", true),
            A("Microsoft.Office.OneNote", "OneNote para Windows 10", "Versión antigua de OneNote", true),
            A("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Control parental de Microsoft", true),
            A("Clipchamp.Clipchamp", "Clipchamp", "Editor de vídeo (solo si no lo usas)", false),
            A("king.com.*", "Juego de King", "Juego preinstalado de King (Candy Crush…)", true),
            A("*TikTok*", "TikTok", "Red social de vídeos (solo si no la usas)", false),
            A("*Instagram*", "Instagram", "Red social (solo si no la usas)", false),
            A("*Facebook*", "Facebook", "Red social (solo si no la usas)", false),
            A("*Disney*", "Disney+", "Series y películas de Disney (solo si no la usas)", false),
            A("*SpotifyAB.SpotifyMusic*", "Spotify", "Música en streaming (solo si no lo usas)", false),
            A("*AmazonVideo.PrimeVideo*", "Prime Video", "Series y películas de Amazon (solo si no la usas)", false),
            A("*Netflix*", "Netflix", "Series y películas (solo si no la usas)", false)
        };

        // Red de seguridad: nunca se tocan aunque algún patrón llegara a coincidir con ellas
        private static readonly string[] NuncaTocar =
        {
            "WindowsStore", "StorePurchase", "Photos", "Calculator", "Notepad", "Terminal", "ScreenSketch",
            "Camera", "Xbox", "Gaming", "YourPhone", "PhoneLink", "Edge", "VCLibs", "NET.Native", "UI.Xaml",
            "DesktopAppInstaller", "SecHealth", "AppRuntime", "Settings", "ImmersiveControlPanel", "ShellExperience"
        };

        private readonly DataGridView tabla;
        private readonly DataGridViewCheckBoxColumn colMarca;
        private readonly DataGridViewTextBoxColumn colApp, colQue, colPaquete;
        private readonly PanelEstado estado;
        private readonly Label lblContador;
        private readonly Boton btnActualizar, btnMarcar, btnQuitar;
        private readonly TableLayoutPanel cuerpo;
        private readonly Tarjeta aviso;
        private readonly Label icoAviso, lblAviso1, lblAviso2;

        private List<AppInstalada> instaladas = new List<AppInstalada>();
        private bool cargando, cargado, quitando, llenando, ajustandoAviso, verTodas;
        private readonly Boton btnVerTodas;

        public PaginaApps() : base("Apps basura", "Apps preinstaladas de Windows que casi nadie usa")
        {
            // ---- Encabezado ----
            btnActualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnActualizar.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnActualizar.Click += delegate { Cargar(); };
            Acciones.Controls.Add(btnActualizar);
            btnVerTodas = new Boton("Ver todas las apps", EstiloBoton.Fantasma, Tema.IcoApps);
            btnVerTodas.Margin = new Padding(Tema.S(8), 0, 0, 0);
            btnVerTodas.Click += delegate
            {
                if (quitando || cargando) return;
                verTodas = !verTodas;
                btnVerTodas.Text = verTodas ? "Ver solo las basura" : "Ver todas las apps";
                Subtitulo = verTodas ? "Todas las apps de Microsoft Store que se pueden desinstalar" : "Apps preinstaladas de Windows que casi nadie usa";
                Llenar();
            };
            Acciones.Controls.Add(btnVerTodas);

            // ---- Aviso informativo ----
            aviso = new Tarjeta();
            aviso.Dock = DockStyle.Fill;
            aviso.Margin = new Padding(0);
            aviso.Padding = Tema.S(18, 14, 18, 14);
            icoAviso = Tema.Icono(Tema.IcoInfo, 15f, Tema.Acento);
            lblAviso1 = EtiquetaAviso("Se desinstalan por completo: la app, sus datos y su copia de Windows para que no vuelva a instalarse sola.",
                Tema.FuenteSemibold(10f), Tema.Texto);
            lblAviso2 = EtiquetaAviso("Ya marcamos las que casi nadie usa. Revisa la lista y desmarca lo que quieras conservar. " +
                "Si algún día la quieres de vuelta, puedes reinstalarla gratis desde Microsoft Store.",
                Tema.Fuente(9.5f), Tema.TextoSuave);
            aviso.Controls.Add(icoAviso);
            aviso.Controls.Add(lblAviso1);
            aviso.Controls.Add(lblAviso2);

            // ---- Tabla ----
            tabla = new DataGridView();
            Tema.EstilizarTabla(tabla);
            tabla.Dock = DockStyle.Fill;
            tabla.Margin = new Padding(0);
            tabla.ReadOnly = false;

            colMarca = new DataGridViewCheckBoxColumn();
            colMarca.HeaderText = "";
            colMarca.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colMarca.Width = Tema.S(52);
            colMarca.Resizable = DataGridViewTriState.False;
            colMarca.SortMode = DataGridViewColumnSortMode.NotSortable;

            colApp = NuevaColumna("App", 27, 150);
            colApp.DefaultCellStyle.Font = Tema.FuenteSemibold(9.5f);
            colQue = NuevaColumna("Qué es", 45, 180);
            colQue.DefaultCellStyle.ForeColor = Tema.TextoSuave;
            colPaquete = NuevaColumna("Paquete", 28, 120);
            colPaquete.DefaultCellStyle.ForeColor = Tema.TextoTenue;
            colPaquete.DefaultCellStyle.Font = Tema.Fuente(9f);

            tabla.Columns.AddRange(new DataGridViewColumn[] { colMarca, colApp, colQue, colPaquete });
            tabla.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (!llenando && e.RowIndex >= 0 && e.ColumnIndex == colMarca.Index) ActualizarContador();
            };
            tabla.CellMouseClick += delegate(object s, DataGridViewCellMouseEventArgs e)
            {
                // Pulsar en cualquier parte de la fila también marca o desmarca. Sobre la propia
                // casilla ya lo hace la tabla, así que ahí solo se atienden los clics fuera de ella.
                if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (e.ColumnIndex == colMarca.Index &&
                    tabla.Rows[e.RowIndex].Cells[e.ColumnIndex].ContentBounds.Contains(e.Location)) return;
                Alternar(tabla.Rows[e.RowIndex]);
            };
            tabla.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space && tabla.CurrentCell != null && tabla.CurrentCell.ColumnIndex != colMarca.Index)
                {
                    Alternar(tabla.Rows[tabla.CurrentCell.RowIndex]);
                    e.Handled = true;
                }
            };

            estado = new PanelEstado();

            Tarjeta tarjeta = new Tarjeta();
            tarjeta.Dock = DockStyle.Fill;
            tarjeta.Margin = new Padding(0);
            tarjeta.Padding = Tema.S(12, 6, 12, 12);
            tarjeta.Controls.Add(tabla);
            tarjeta.Controls.Add(estado);

            // ---- Barra inferior ----
            lblContador = new Label();
            lblContador.AutoSize = false;
            lblContador.Dock = DockStyle.Fill;
            lblContador.TextAlign = ContentAlignment.MiddleLeft;
            lblContador.AutoEllipsis = true;
            lblContador.UseMnemonic = false;
            lblContador.Font = Tema.Fuente(9.5f);
            lblContador.ForeColor = Tema.TextoSuave;
            lblContador.Margin = new Padding(Tema.S(4), 0, Tema.S(16), 0);

            btnMarcar = new Boton("Marcar todas", EstiloBoton.Fantasma);
            btnMarcar.Margin = new Padding(0, 0, Tema.S(8), 0);
            btnMarcar.Click += delegate { MarcarTodas(); };

            btnQuitar = new Boton("Quitar seleccionadas", EstiloBoton.Peligro, IcoPapelera);
            btnQuitar.Margin = new Padding(0);
            btnQuitar.Click += Quitar_Click;

            TableLayoutPanel abajo = new TableLayoutPanel();
            abajo.Dock = DockStyle.Fill;
            abajo.AutoSize = true;
            abajo.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            abajo.Margin = new Padding(0, Tema.S(16), 0, 0);
            abajo.Padding = new Padding(0);
            abajo.RowCount = 1;
            abajo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            abajo.ColumnCount = 3;
            abajo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            abajo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            abajo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            abajo.Controls.Add(lblContador, 0, 0);
            abajo.Controls.Add(btnMarcar, 1, 0);
            abajo.Controls.Add(btnQuitar, 2, 0);

            // ---- Todo junto ----
            cuerpo = new TableLayoutPanel();
            cuerpo.Dock = DockStyle.Fill;
            cuerpo.Margin = new Padding(0);
            cuerpo.Padding = new Padding(0);
            cuerpo.ColumnCount = 1;
            cuerpo.RowCount = 4;
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(72)));   // aviso (se recalcula con el ancho)
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(16)));   // separación
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cuerpo.Controls.Add(aviso, 0, 0);
            cuerpo.Controls.Add(tarjeta, 0, 2);
            cuerpo.Controls.Add(abajo, 0, 3);
            cuerpo.SizeChanged += delegate { AjustarAviso(); };
            Contenido.Controls.Add(cuerpo);

            AjustarAviso();
            MostrarEstado();
            estado.Cargando("Buscando apps preinstaladas…");
            ActualizarContador();
        }

        public override void AlMostrar()
        {
            // Solo lee la lista de apps, y solo la primera vez (luego con Actualizar o tras quitar)
            if (!cargado && !cargando && !quitando) Cargar();
        }

        // ------------------------------------------------------------------ construcción

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

        private static Label EtiquetaAviso(string texto, Font fuente, Color color)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Text = texto;
            l.Font = fuente;
            l.ForeColor = color;
            l.UseMnemonic = false;
            l.Margin = new Padding(0);
            return l;
        }

        // El texto del aviso se reparte en varias líneas si la ventana es estrecha
        private void AjustarAviso()
        {
            if (ajustandoAviso) return;
            ajustandoAviso = true;
            try
            {
                int ancho = cuerpo.ClientSize.Width;
                if (ancho <= 0) return;
                int x0 = aviso.Padding.Left, y0 = aviso.Padding.Top;
                icoAviso.Location = new Point(x0, y0 + Tema.S(1));
                int xt = x0 + icoAviso.PreferredWidth + Tema.S(12);
                int anchoTexto = Math.Max(Tema.S(120), ancho - xt - aviso.Padding.Right);
                int alto1 = AltoTexto(lblAviso1, anchoTexto);
                int alto2 = AltoTexto(lblAviso2, anchoTexto);
                lblAviso1.Bounds = new Rectangle(xt, y0, anchoTexto, alto1);
                lblAviso2.Bounds = new Rectangle(xt, y0 + alto1 + Tema.S(3), anchoTexto, alto2);
                float alto = y0 + alto1 + Tema.S(3) + alto2 + aviso.Padding.Bottom;
                if (Math.Abs(cuerpo.RowStyles[0].Height - alto) > 0.5f) cuerpo.RowStyles[0].Height = alto;
            }
            finally
            {
                ajustandoAviso = false;
            }
        }

        private static int AltoTexto(Label l, int ancho)
        {
            TextFormatFlags f = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            return TextRenderer.MeasureText(l.Text, l.Font, new Size(ancho, int.MaxValue), f).Height + Tema.S(2);
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

        // ------------------------------------------------------------------ carga (solo lectura)

        private async void Cargar()
        {
            await CargarAsync();
        }

        private async Task CargarAsync()
        {
            if (cargando || quitando) return;
            cargando = true;
            btnActualizar.Enabled = false;
            if (!cargado)
            {
                MostrarEstado();
                estado.Cargando("Buscando apps preinstaladas…");
            }
            else
            {
                lblContador.Text = "Actualizando…";
            }
            ActualizarContador();
            try
            {
                string script = ConstruirScript();
                List<string[]> filas = await Task.Run(() => Util.PowerShellFilas(script, 90000));
                if (IsDisposed) return;

                List<AppInstalada> lista = Procesar(filas);
                if (lista == null)
                {
                    Util.Log("Apps: no se pudo leer la lista de apps instaladas");
                    if (!cargado)
                    {
                        MostrarEstado();
                        estado.Mensaje(Tema.IcoAviso, Tema.Peligro, "No se pudo leer la lista de apps",
                            "Pulsa Actualizar para intentarlo otra vez.");
                    }
                    else
                    {
                        Dialogo.Error(FindForm(), "No se pudo actualizar la lista", "Vuelve a intentarlo en un momento.");
                    }
                    return;
                }
                instaladas = lista;
                cargado = true;
                Llenar();
            }
            catch (Exception ex)
            {
                Util.Log("Apps: error al leer la lista: " + ex);
                if (IsDisposed) return;
                if (!cargado)
                {
                    MostrarEstado();
                    estado.Mensaje(Tema.IcoAviso, Tema.Peligro, "No se pudo leer la lista de apps",
                        "Pulsa Actualizar para intentarlo otra vez.");
                }
            }
            finally
            {
                cargando = false;
                if (!IsDisposed)
                {
                    btnActualizar.Enabled = !quitando;
                    ActualizarContador();
                }
            }
        }

        // Una sola llamada a PowerShell: se listan los paquetes una vez y se comparan con cada patrón
        private static string ConstruirScript()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("$todas = @(Get-AppxPackage)\n");
            sb.Append("L '__total' $todas.Count\n");
            sb.Append("$patrones = @(");
            for (int i = 0; i < Conocidas.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Util.ComillasPS(Conocidas[i].Patron));
            }
            sb.Append(")\n");
            sb.Append("foreach ($x in $patrones) {\n");
            sb.Append("  $todas | Where-Object { $_.Name -like $x -and -not $_.NonRemovable -and -not $_.IsFramework } |\n");
            sb.Append("    ForEach-Object { L $x $_.Name $_.PackageFullName $_.PackageFamilyName }\n");
            sb.Append("}\n");
            // Todas las apps de la Tienda que se pueden quitar (las del sistema y los componentes no)
            sb.Append("$todas | Where-Object { -not $_.NonRemovable -and -not $_.IsFramework -and -not $_.IsResourcePackage -and ");
            sb.Append("$_.SignatureKind -ne 'System' } | ForEach-Object {\n");
            sb.Append("  $n = $null; try { $n = (Get-AppxPackageManifest $_).Package.Properties.DisplayName } catch { }\n");
            sb.Append("  if (-not $n -or $n -like 'ms-resource*') { $n = $_.Name }\n");
            sb.Append("  L '__app' $_.Name $_.PackageFullName $_.PackageFamilyName $n\n");
            sb.Append("}\n");
            sb.Append("L '__fin'\n");
            return sb.ToString();
        }

        /// <summary>Convierte la salida de PowerShell en la lista de apps. null si la lectura falló.</summary>
        private static List<AppInstalada> Procesar(List<string[]> filas)
        {
            int total = -1;
            bool fin = false;
            Dictionary<string, AppInstalada> porPaquete = new Dictionary<string, AppInstalada>(StringComparer.OrdinalIgnoreCase);
            List<AppInstalada> lista = new List<AppInstalada>();
            foreach (string[] f in filas)
            {
                if (f.Length == 0) continue;
                string clave = f[0].Trim().TrimStart('﻿');
                if (clave == "__fin") { fin = true; continue; }
                if (clave == "__total")
                {
                    if (f.Length > 1) int.TryParse(f[1].Trim(), out total);
                    continue;
                }
                if (f.Length < 3) continue;
                bool general = clave == "__app";
                AppConocida info = general ? null : BuscarConocida(clave);
                if (!general && info == null) continue;
                string paquete = f[1].Trim(), completo = f[2].Trim();
                string familia = f.Length > 3 ? f[3].Trim() : "";
                if (paquete.Length == 0 || completo.Length == 0 || EsProtegida(paquete)) continue;

                AppInstalada a;
                if (!porPaquete.TryGetValue(paquete, out a))
                {
                    if (general)
                    {
                        string visible = f.Length > 4 && f[4].Trim().Length > 0 ? f[4].Trim() : paquete;
                        info = A(paquete, visible, "App de Microsoft Store", false);
                    }
                    a = new AppInstalada();
                    a.Info = info;
                    a.Paquete = paquete;
                    a.Nombre = NombreAmigable(info, paquete);
                    a.Familia = familia;
                    a.Conocida = !general;
                    porPaquete[paquete] = a;
                    lista.Add(a);
                }
                if (!a.Completos.Contains(completo)) a.Completos.Add(completo);
                if (string.IsNullOrEmpty(a.Familia)) a.Familia = familia;
            }
            lista.Sort(delegate(AppInstalada x, AppInstalada y)
            {
                if (x.Conocida != y.Conocida) return x.Conocida ? -1 : 1;
                return x.Conocida ? 0 : string.Compare(x.Nombre, y.Nombre, StringComparison.CurrentCultureIgnoreCase);
            });
            // Todo Windows tiene paquetes: si no salió ninguno, la lectura falló
            if (!fin || total <= 0) return null;
            return lista;
        }

        private static AppConocida BuscarConocida(string patron)
        {
            foreach (AppConocida c in Conocidas)
            {
                if (string.Equals(c.Patron, patron, StringComparison.Ordinal)) return c;
            }
            return null;
        }

        private static bool EsProtegida(string paquete)
        {
            if (string.IsNullOrEmpty(paquete)) return true;
            if (string.Equals(paquete, "Microsoft.Paint", StringComparison.OrdinalIgnoreCase)) return true;   // el Paint de siempre
            foreach (string n in NuncaTocar)
            {
                if (paquete.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // "king.com.CandyCrushSodaSaga" -> "Candy Crush Soda Saga (King)"
        private static string NombreAmigable(AppConocida info, string paquete)
        {
            const string prefijoKing = "king.com.";
            if (info.Patron == "king.com.*" && paquete.Length > prefijoKing.Length &&
                paquete.StartsWith(prefijoKing, StringComparison.OrdinalIgnoreCase))
            {
                string resto = paquete.Substring(prefijoKing.Length);
                resto = Regex.Replace(resto, "(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Za-z])", " ");
                return resto + " (King)";
            }
            return info.Nombre;
        }

        // ------------------------------------------------------------------ tabla

        private void Llenar()
        {
            // Se respeta lo que el usuario ya hubiera marcado o desmarcado
            Dictionary<string, bool> antes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in tabla.Rows)
            {
                AppInstalada a = r.Tag as AppInstalada;
                if (a != null) antes[a.Paquete] = Marcada(r);
            }

            llenando = true;
            try
            {
                tabla.EndEdit();
                tabla.Rows.Clear();
                List<DataGridViewRow> filas = new List<DataGridViewRow>();
                foreach (AppInstalada a in instaladas)
                {
                    if (!verTodas && !a.Conocida) continue;
                    bool marcada;
                    if (!antes.TryGetValue(a.Paquete, out marcada)) marcada = a.Info.Recomendada;
                    DataGridViewRow r = (DataGridViewRow)tabla.RowTemplate.Clone();
                    r.CreateCells(tabla, marcada, a.Nombre, a.Info.Descripcion, a.Paquete);
                    r.Tag = a;
                    filas.Add(r);
                }
                tabla.Rows.AddRange(filas.ToArray());
                if (tabla.Rows.Count > 0) MostrarTabla();
                tabla.ClearSelection();
                tabla.CurrentCell = null;
            }
            finally
            {
                llenando = false;
            }

            if (tabla.Rows.Count == 0)
            {
                MostrarEstado();
                estado.Mensaje(Tema.IcoCheck, Tema.Exito, "No hay apps basura instaladas. ¡Bien!",
                    "Tu Windows no tiene ninguna de las apps preinstaladas que casi nadie usa.");
            }
            ActualizarContador();
        }

        private bool Marcada(DataGridViewRow r)
        {
            object v = r.Cells[colMarca.Index].Value;
            return v is bool && (bool)v;
        }

        private void Alternar(DataGridViewRow r)
        {
            if (quitando || cargando) return;
            tabla.EndEdit();
            r.Cells[colMarca.Index].Value = !Marcada(r);
        }

        private List<AppInstalada> Elegidas()
        {
            List<AppInstalada> l = new List<AppInstalada>();
            foreach (DataGridViewRow r in tabla.Rows)
            {
                AppInstalada a = r.Tag as AppInstalada;
                if (a != null && Marcada(r)) l.Add(a);
            }
            return l;
        }

        private void MarcarTodas()
        {
            if (quitando || cargando || tabla.Rows.Count == 0) return;
            tabla.EndEdit();
            bool todas = Elegidas().Count == tabla.Rows.Count;
            llenando = true;
            try
            {
                foreach (DataGridViewRow r in tabla.Rows) r.Cells[colMarca.Index].Value = !todas;
            }
            finally
            {
                llenando = false;
            }
            ActualizarContador();
        }

        private void ActualizarContador()
        {
            int total = tabla.Rows.Count;
            int n = Elegidas().Count;
            if (!quitando && !(cargando && cargado))
            {
                lblContador.Text = total == 0 ? "" : "Seleccionadas: " + n + " de " + total;
            }
            btnQuitar.Enabled = n > 0 && !quitando && !cargando;
            btnMarcar.Enabled = total > 0 && !quitando && !cargando;
            btnMarcar.Text = total > 0 && n == total ? "Desmarcar todas" : "Marcar todas";
        }

        // ------------------------------------------------------------------ quitar

        private async void Quitar_Click(object sender, EventArgs e)
        {
            if (quitando || cargando) return;
            tabla.EndEdit();
            List<AppInstalada> elegidas = Elegidas();
            if (elegidas.Count == 0) return;
            try
            {
                const int maximo = 12;
                StringBuilder lista = new StringBuilder();
                for (int i = 0; i < elegidas.Count && i < maximo; i++) lista.Append("•  ").Append(elegidas[i].Nombre).Append('\n');
                if (elegidas.Count > maximo) lista.Append("…y ").Append(elegidas.Count - maximo).Append(" más\n");
                string titulo = elegidas.Count == 1 ? "¿Quitar " + elegidas[0].Nombre + "?" : "¿Quitar " + elegidas.Count + " apps?";
                string mensaje = (elegidas.Count == 1 ? "Se desinstalará por completo:" : "Se desinstalarán por completo:") + "\n\n" + lista +
                                 "\nSe borran también sus datos" +
                                 (Util.EsAdmin ? " y la copia que guarda Windows para volver a instalarlas solas." : ".") +
                                 "\nPuedes volver a instalarlas gratis desde Microsoft Store.";
                if (!Dialogo.Confirmar(FindForm(), titulo, mensaje, "Quitar", true)) return;

                quitando = true;
                colMarca.ReadOnly = true;
                btnActualizar.Enabled = false;
                ActualizarContador();

                List<string> quitadas = new List<string>();
                List<string> fallos = new List<string>();
                List<Resto> restos = new List<Resto>();
                IProgress<string> progreso = new Progress<string>(delegate(string t)
                {
                    if (!IsDisposed && quitando) lblContador.Text = t;
                });
                await Task.Run(() =>
                {
                    for (int i = 0; i < elegidas.Count; i++)
                    {
                        AppInstalada a = elegidas[i];
                        progreso.Report("Quitando " + a.Nombre + "…  (" + (i + 1) + " de " + elegidas.Count + ")");
                        string error = QuitarApp(a);
                        if (error == null)
                        {
                            quitadas.Add(a.Nombre);
                            progreso.Report("Buscando restos de " + a.Nombre + "…");
                            restos.AddRange(RestosDeApp(a));
                        }
                        else fallos.Add(a.Nombre + ": " + error);
                    }
                });
                if (IsDisposed) return;

                quitando = false;
                colMarca.ReadOnly = false;
                string nombreQuitadas = quitadas.Count == 1 ? quitadas[0] : quitadas.Count + " apps";
                if (restos.Count > 0)
                {
                    // Se enseñan los restos para elegir qué borrar y después un informe de lo borrado
                    List<Resto> elegidos = VentanaRestos.Elegir(FindForm(), nombreQuitadas, restos);
                    if (elegidos != null)
                    {
                        ResultadoBorradoRestos res = await Task.Run(() => Residuos.Borrar(elegidos, nombreQuitadas));
                        if (IsDisposed) return;
                        VentanaRestos.Informe(FindForm(), nombreQuitadas, elegidos, res.CarpetaRespaldo);
                    }
                }
                if (fallos.Count == 0)
                {
                    if (restos.Count == 0)
                    {
                        Dialogo.Exito(FindForm(), "Listo",
                            (quitadas.Count == 1 ? "Se quitó " + quitadas[0] : "Se quitaron " + quitadas.Count + " apps") + " y no dejaron restos.");
                    }
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    if (quitadas.Count > 0) sb.Append(quitadas.Count == 1 ? "Se quitó 1 app, pero estas no:" : "Se quitaron " + quitadas.Count + " apps, pero estas no:");
                    else sb.Append(fallos.Count == 1 ? "No se pudo quitar:" : "No se pudo quitar ninguna:");
                    sb.Append("\n\n");
                    for (int i = 0; i < fallos.Count && i < maximo; i++) sb.Append("•  ").Append(fallos[i]).Append('\n');
                    if (fallos.Count > maximo) sb.Append("…y ").Append(fallos.Count - maximo).Append(" más\n");
                    sb.Append("\nSi alguna está abierta, ciérrala y vuelve a intentarlo.");
                    Dialogo.Error(FindForm(), quitadas.Count > 0 ? "Algunas apps no se pudieron quitar" : "No se pudieron quitar las apps", sb.ToString());
                }
                if (IsDisposed) return;
                await CargarAsync();
            }
            catch (Exception ex)
            {
                Util.Log("Apps: error al quitar apps: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "Algo salió mal", "No se pudieron quitar las apps. El detalle se guardó en el registro de la app.");
            }
            finally
            {
                quitando = false;
                if (!IsDisposed)
                {
                    colMarca.ReadOnly = false;
                    btnActualizar.Enabled = !cargando;
                    ActualizarContador();
                }
            }
        }

        /// <summary>Quita una app del usuario actual. Devuelve null si fue bien o el motivo del fallo. Bloquea.</summary>
        private static string QuitarApp(AppInstalada a)
        {
            if (EsProtegida(a.Paquete))
            {
                Util.Log("Apps: '" + a.Paquete + "' está protegida; no se toca");
                return "es parte de Windows y no se toca";
            }
            foreach (string completo in a.Completos)
            {
                // Se comprueba justo antes de quitarla que sigue instalada y que Windows deja quitarla.
                // Como administrador se quita para todos los usuarios y también su copia "aprovisionada"
                // (la que Windows usa para volver a instalarla sola); si -AllUsers falla, solo del usuario actual.
                string pq = Util.ComillasPS(a.Paquete), pc = Util.ComillasPS(completo);
                string script =
                    "$p = Get-AppxPackage -Name " + pq + " | Where-Object { $_.PackageFullName -eq " + pc + " } | Select-Object -First 1\n" +
                    "if (-not $p) { L 'nohay' }\n" +
                    "elseif ($p.NonRemovable -or $p.IsFramework) { L 'error' 'Windows no deja quitarla' }\n" +
                    "else {\n" +
                    "  try {\n" +
                    (Util.EsAdmin
                        ? "    try { Remove-AppxPackage -Package " + pc + " -AllUsers -ErrorAction Stop } catch { Remove-AppxPackage -Package " + pc + " -ErrorAction Stop }\n"
                        : "    Remove-AppxPackage -Package " + pc + " -ErrorAction Stop\n") +
                    "    L 'ok'\n" +
                    "  } catch { L 'error' $_.Exception.Message }\n" +
                    "}\n" +
                    (Util.EsAdmin
                        ? "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq " + pq + " } | ForEach-Object {\n" +
                          "  try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -AllUsers -ErrorAction Stop | Out-Null; L 'prov' $_.PackageName }\n" +
                          "  catch { try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null; L 'prov' $_.PackageName } catch { } }\n" +
                          "}\n"
                        : "");
                ResultadoProceso r = Util.EjecutarPowerShell(script, 180000);

                string resultado = null, detalle = null;
                foreach (string linea in r.Salida.Split('\n'))
                {
                    string l = linea.Trim().TrimStart('﻿');
                    if (l.Length == 0) continue;
                    string[] partes = l.Split('\t');
                    if (partes[0] == "ok" || partes[0] == "nohay" || partes[0] == "error")
                    {
                        resultado = partes[0];
                        detalle = partes.Length > 1 ? partes[1] : null;
                    }
                }

                if (resultado == "ok" || resultado == "nohay")
                {
                    Util.Log("Apps: quitada '" + a.Nombre + "' (" + completo + ")" + (resultado == "nohay" ? " (ya no estaba)" : ""));
                    continue;
                }
                string motivo = resultado == "error" ? detalle : r.Error.Trim();
                Util.Log("Apps: no se pudo quitar '" + a.Nombre + "' (" + completo + "), código " + r.Codigo + ": " + motivo);
                return MotivoAmigable(motivo, r.Codigo);
            }
            return null;
        }

        /// <summary>
        /// Lo que deja una app quitada: su carpeta de datos en %LOCALAPPDATA%\Packages y su acceso de
        /// ejecución en WindowsApps. No se borra nada aquí: se enseña al usuario para que elija. Bloquea.
        /// </summary>
        private static List<Resto> RestosDeApp(AppInstalada a)
        {
            List<Resto> l = new List<Resto>();
            try
            {
                if (string.IsNullOrEmpty(a.Familia) || a.Familia.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || a.Familia.Contains("..")) return l;
                // Si la app sigue instalada (otra versión), sus datos no son restos
                ResultadoProceso r = Util.EjecutarPowerShell("if (Get-AppxPackage -Name " + Util.ComillasPS(a.Paquete) + ") { L 'sigue' } else { L 'no' }", 60000);
                if (r.Salida.Contains("sigue") || !r.Salida.Contains("no")) return l;
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[,] sitios =
                {
                    { Path.Combine(Path.Combine(lad, "Packages"), a.Familia), "Datos guardados de " + a.Nombre },
                    { Path.Combine(lad, @"Microsoft\WindowsApps\" + a.Familia), "Acceso de ejecución de " + a.Nombre }
                };
                for (int i = 0; i < sitios.GetLength(0); i++)
                {
                    string ruta = sitios[i, 0];
                    if (!Directory.Exists(ruta) || Util.EsRutaProtegida(ruta)) continue;
                    Resto resto = new Resto();
                    resto.Tipo = TipoResto.Carpeta;
                    resto.Ruta = ruta;
                    resto.Motivo = sitios[i, 1];
                    resto.Tamano = Util.TamanoCarpeta(ruta, System.Threading.CancellationToken.None);
                    l.Add(resto);
                }
            }
            catch (Exception ex)
            {
                Util.Log("Apps: error al buscar los restos de '" + a.Nombre + "': " + ex.Message);
            }
            return l;
        }

        private static string MotivoAmigable(string motivo, int codigo)
        {
            if (string.IsNullOrEmpty(motivo))
            {
                return codigo == -1 ? "no respondió a tiempo" : "Windows no dejó quitarla";
            }
            Match hr = Regex.Match(motivo, "0x[0-9A-Fa-f]{8}");
            if (hr.Success) return "Windows no dejó quitarla (código " + hr.Value.ToUpperInvariant().Replace("0X", "0x") + ")";
            string primera = motivo.Split('\n')[0].Trim();
            return "Windows no dejó quitarla: " + Util.Recortar(primera, 120);
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
