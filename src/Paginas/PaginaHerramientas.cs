using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OptimizadorPC
{
    /// <summary>Herramientas avanzadas: punto de restauración, DISM, hibernación, Sensor de almacenamiento, tarea semanal...</summary>
    class PaginaHerramientas : PaginaBase
    {
        private const string NombreTarea = "OptimizadorPC_LimpiezaSemanal";
        private const string ClaveSensor = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";
        private const string SinAdmin = "Necesita permisos de administrador";

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        // Lo que se lee en segundo plano cada vez que se muestra la página
        private class Estados
        {
            public long Hiber = -1;
            public int Sensor = -1, Frecuencia = -1;
            public bool Tarea;
            public string RutaTarea;
            public bool WindowsOld;
            public long LogTamano = -1;
            public DateTime LogFecha;
        }

        private readonly Panel desplazable, lienzo;
        private readonly List<TarjetaHerramienta> tarjetas = new List<TarjetaHerramienta>();
        private TarjetaHerramienta tPunto, tDism, tHiber, tSensor, tSemanal, tLiberador, tRegistro;
        private Boton btnPunto, btnDism, btnHiber, btnProgramar, btnQuitar;
        private Interruptor swSensor;

        private bool refrescando, pendiente, reordenando, otraVez;
        private bool ocupadoPunto, ocupadoDism, ocupadoHiber, ocupadoSensor, ocupadoSemanal;
        private bool hiberActiva;
        private long tamHiber = -1;
        private int lote;

        public PaginaHerramientas() : base("Herramientas", "Opciones avanzadas para ganar espacio y automatizar")
        {
            desplazable = new Panel();
            desplazable.Dock = DockStyle.Fill;
            desplazable.AutoScroll = true;
            lienzo = new Panel();
            lienzo.Location = new Point(0, 0);
            desplazable.Controls.Add(lienzo);
            Contenido.Controls.Add(desplazable);
            desplazable.Resize += delegate(object s, EventArgs e) { Reordenar(); };
            desplazable.HandleCreated += delegate(object s, EventArgs e) { Tema.ScrollOscuro(desplazable); };

            lote++;
            CrearTarjetas();
            EstadoInicial();
            lote--;
            Reordenar();
        }

        // ------------------------------------------------------------------ construcción

        private void CrearTarjetas()
        {
            tPunto = Nueva(Tema.IcoEscudo, "Punto de restauración",
                "Guarda una copia de la configuración de Windows por si algo sale mal.");
            btnPunto = Agregar(tPunto, "Crear punto", EstiloBoton.Primario, null, CrearPunto);

            tDism = Nueva(Tema.IcoLimpieza, "Limpieza profunda de Windows",
                "Borra versiones viejas de archivos de Windows que dejan las actualizaciones (WinSxS). Tarda entre 5 y 20 minutos.");
            btnDism = Agregar(tDism, "Empezar", EstiloBoton.Secundario, Tema.IcoPlay, LimpiezaProfunda);

            tHiber = Nueva(Tema.IcoReloj, "Hibernación", "Comprobando el archivo de hibernación…");
            btnHiber = Agregar(tHiber, "Desactivar", EstiloBoton.Peligro, null, CambiarHibernacion);

            tSensor = Nueva(Tema.IcoDisco, "Sensor de almacenamiento",
                "El limpiador automático de Windows: borra los archivos temporales y lo que lleva más de 30 días en la papelera.");
            swSensor = new Interruptor();
            swSensor.Enabled = false;
            swSensor.CambioEstado += SensorCambiado;
            tSensor.PonerExtra(swSensor);
            Agregar(tSensor, "Abrir en Configuración", EstiloBoton.Fantasma, null, delegate
            {
                if (!Util.Abrir("ms-settings:storagesense", null))
                {
                    Dialogo.Error(FindForm(), "No se pudo abrir Configuración", "Ábrela tú en Inicio > Configuración > Sistema > Almacenamiento.");
                }
            });

            tSemanal = Nueva(Tema.IcoPlay, "Limpieza automática semanal",
                "El optimizador limpiará lo recomendado cada domingo a las 12:00 (o al encender el PC si estaba apagado).");
            tSemanal.PonerNotas(Application.ExecutablePath, "Si mueves la app a otra carpeta, vuelve a programarla.");
            btnProgramar = Agregar(tSemanal, "Programar", EstiloBoton.Primario, null, ProgramarTarea);
            btnQuitar = Agregar(tSemanal, "Quitar", EstiloBoton.Secundario, null, QuitarTarea);
            btnQuitar.Visible = false;

            tLiberador = Nueva(Tema.IcoHerramientas, "Liberador de espacio de Windows",
                "La herramienta clásica de Windows. Sirve para borrar Windows.old (instalaciones anteriores, suele ocupar 10–30 GB).");
            Agregar(tLiberador, "Abrir", EstiloBoton.Secundario, null, AbrirLiberador);

            tAccesos = Nueva(Tema.IcoAccesoDirecto, "Accesos directos",
                "Pon el optimizador en el Escritorio y en el menú Inicio para abrirlo sin buscar la carpeta.");
            btnAccesos = Agregar(tAccesos, "Crear accesos directos", EstiloBoton.Primario, null, CrearAccesos);
            btnQuitarAccesos = Agregar(tAccesos, "Quitar", EstiloBoton.Secundario, null, QuitarAccesos);

            tSonidos = Nueva(Tema.IcoSonido, "Sonidos de la app",
                "Un sonido al terminar cada limpieza, distinto según el tema: campanita, risa de bruja o caja de música rota.");
            swSonidos = new Interruptor();
            swSonidos.Establecer(Sonidos.Activados);
            swSonidos.CambioEstado += delegate
            {
                Sonidos.Activados = swSonidos.Activado;
                tSonidos.PonerEstado(swSonidos.Activado ? "Activados" : "Desactivados", swSonidos.Activado ? Tema.Exito : Tema.TextoTenue);
                if (swSonidos.Activado) Sonidos.Probar();
            };
            tSonidos.PonerExtra(swSonidos);
            Agregar(tSonidos, "Probar sonido", EstiloBoton.Fantasma, Tema.IcoPlay, delegate { Sonidos.Probar(); });

            tRegistro = Nueva(Tema.IcoInfo, "Registro de la app", "Todo lo que hace el optimizador queda anotado aquí.");
            tRegistro.PonerNotas(Util.ArchivoLog, "");
            Agregar(tRegistro, "Abrir registro", EstiloBoton.Fantasma, null, AbrirRegistro);
        }

        // ------------------------------------------------------------------ accesos directos

        private TarjetaHerramienta tAccesos, tSonidos;
        private Boton btnAccesos, btnQuitarAccesos;
        private Interruptor swSonidos;

        private static string[] RutasAccesos()
        {
            return new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Optimizador de PC.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Optimizador de PC.lnk")
            };
        }

        private void ActualizarAccesos()
        {
            int existen = 0;
            bool otraRuta = false;
            foreach (string r in RutasAccesos())
            {
                if (!File.Exists(r)) continue;
                existen++;
                string destino = Residuos.DestinoAcceso(r);
                if (destino != null && !string.Equals(destino, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) otraRuta = true;
            }
            if (existen == 0) tAccesos.PonerEstado("Todavía no hay accesos directos", Tema.TextoTenue);
            else if (otraRuta) tAccesos.PonerEstado("Apuntan a otra carpeta: vuelve a crearlos", Tema.Aviso);
            else tAccesos.PonerEstado(existen == 2 ? "En el Escritorio y en el menú Inicio" : "Solo uno de los dos: vuelve a crearlos", existen == 2 ? Tema.Exito : Tema.Aviso);
            btnAccesos.Text = existen == 0 ? "Crear accesos directos" : "Volver a crear";
            btnQuitarAccesos.Visible = existen > 0;
            tSonidos.PonerEstado(Sonidos.Activados ? "Activados" : "Desactivados", Sonidos.Activados ? Tema.Exito : Tema.TextoTenue);
        }

        private void CrearAccesos()
        {
            List<string> fallos = new List<string>();
            foreach (string r in RutasAccesos())
            {
                try
                {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    object shell = Activator.CreateInstance(t);
                    object acceso = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { r });
                    Type ta = acceso.GetType();
                    ta.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, acceso, new object[] { Application.ExecutablePath });
                    ta.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, acceso, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                    ta.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, acceso, new object[] { Application.ExecutablePath + ",0" });
                    ta.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, acceso, new object[] { "Optimizador de PC · por Biliman" });
                    ta.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, acceso, null);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(acceso);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    Util.Log("Acceso directo creado: " + r);
                }
                catch (Exception ex)
                {
                    fallos.Add(Path.GetDirectoryName(r));
                    Util.Log("No se pudo crear el acceso directo " + r + ": " + ex.Message);
                }
            }
            ActualizarAccesos();
            if (fallos.Count == 0) Dialogo.Exito(FindForm(), "Accesos directos creados", "Ya tienes el optimizador en el Escritorio y en el menú Inicio.");
            else Dialogo.Error(FindForm(), "No se pudieron crear todos", "Falló en: " + string.Join(", ", fallos.ToArray()));
        }

        private void QuitarAccesos()
        {
            foreach (string r in RutasAccesos())
            {
                try
                {
                    // Solo se borra si de verdad es un acceso directo a esta app
                    string destino = Residuos.DestinoAcceso(r);
                    if (File.Exists(r) && destino != null &&
                        (string.Equals(destino, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                         Path.GetFileName(destino).StartsWith("OptimizadorPC", StringComparison.OrdinalIgnoreCase)))
                    {
                        File.Delete(r);
                        Util.Log("Acceso directo quitado: " + r);
                    }
                }
                catch { }
            }
            ActualizarAccesos();
        }

        private TarjetaHerramienta Nueva(string glifo, string titulo, string descripcion)
        {
            TarjetaHerramienta t = new TarjetaHerramienta(glifo, titulo, descripcion);
            t.AltoCambiado += delegate(object s, EventArgs e) { Reordenar(); };
            tarjetas.Add(t);
            lienzo.Controls.Add(t);
            return t;
        }

        private static Boton Agregar(TarjetaHerramienta t, string texto, EstiloBoton estilo, string icono, Action accion)
        {
            Boton b = new Boton(texto, estilo, icono);
            b.Click += delegate(object s, EventArgs e) { accion(); };
            t.Botones.Controls.Add(b);
            return b;
        }

        // Rejilla de dos columnas; las dos tarjetas de cada fila miden lo mismo
        private void Reordenar()
        {
            if (lote > 0 || lienzo == null || tarjetas.Count == 0) return;
            if (reordenando) { otraVez = true; return; }
            reordenando = true;
            try
            {
                // La barra de desplazamiento puede aparecer o desaparecer al recolocar: se repite como mucho 3 veces
                for (int i = 0; i < 3; i++)
                {
                    otraVez = false;
                    ReordenarUnaVez();
                    if (!otraVez) break;
                }
            }
            finally
            {
                reordenando = false;
            }
        }

        private void ReordenarUnaVez()
        {
            int ancho = desplazable.ClientSize.Width;
            if (desplazable.VerticalScroll.Visible) ancho -= Tema.S(12);
            if (ancho < Tema.S(200)) return;
            int hueco = Tema.S(16);
            int col = (ancho - hueco) / 2;
            int anchoB = ancho - col - hueco;
            int y = 0;
            for (int i = 0; i < tarjetas.Count; i += 2)
            {
                TarjetaHerramienta a = tarjetas[i];
                TarjetaHerramienta b = i + 1 < tarjetas.Count ? tarjetas[i + 1] : null;
                int alto = a.AltoNecesario(col);
                if (b != null) alto = Math.Max(alto, b.AltoNecesario(anchoB));
                a.Bounds = new Rectangle(0, y, col, alto);
                if (b != null) b.Bounds = new Rectangle(col + hueco, y, anchoB, alto);
                y += alto + hueco;
            }
            lienzo.Size = new Size(ancho, Math.Max(1, y - hueco));
        }

        // ------------------------------------------------------------------ estados

        private void EstadoInicial()
        {
            bool admin = Util.EsAdmin;
            tPunto.PonerEstado(admin ? "Comprobando…" : SinAdmin, admin ? Tema.TextoTenue : Tema.Aviso);
            btnPunto.Enabled = admin;
            tDism.PonerEstado(admin ? "Lista para empezar" : SinAdmin, admin ? Tema.TextoTenue : Tema.Aviso);
            btnDism.Enabled = admin;
            tHiber.PonerEstado("Comprobando…", Tema.TextoTenue);
            btnHiber.Enabled = false;
            tSensor.PonerEstado("Comprobando…", Tema.TextoTenue);
            tSemanal.PonerEstado("Comprobando…", Tema.TextoTenue);
            btnProgramar.Enabled = false;
            tLiberador.PonerEstado("Comprobando…", Tema.TextoTenue);
            tRegistro.PonerEstado("Comprobando…", Tema.TextoTenue);
        }

        public override void AlMostrar()
        {
            // Solo lectura: tamaños de archivos, registro y consulta de la tarea programada
            ActualizarAccesos();
            RefrescarEstados();
        }

        private async void RefrescarEstados()
        {
            if (refrescando) { pendiente = true; return; }
            refrescando = true;
            try
            {
                do
                {
                    pendiente = false;
                    Estados e = await Task.Run(() => LeerEstados());
                    if (IsDisposed) return;
                    Aplicar(e);
                    if (Util.EsAdmin && !ocupadoPunto)
                    {
                        string[] punto = await Task.Run(() => LeerUltimoPunto());
                        if (IsDisposed) return;
                        AplicarPunto(punto);
                    }
                } while (pendiente);
            }
            catch (Exception ex)
            {
                Util.Log("Herramientas: error al leer el estado: " + ex);
            }
            finally
            {
                refrescando = false;
            }
        }

        private static Estados LeerEstados()
        {
            Estados e = new Estados();
            e.Hiber = Sistema.TamanoArchivoRaiz("hiberfil.sys");
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ClaveSensor))
                {
                    if (k == null)
                    {
                        e.Sensor = 0;
                    }
                    else
                    {
                        object v = k.GetValue("01");
                        e.Sensor = v is int && (int)v == 1 ? 1 : 0;
                        object f = k.GetValue("2048");
                        if (f is int) e.Frecuencia = (int)f;
                    }
                }
            }
            catch { e.Sensor = -1; }

            ResultadoProceso r = Util.EjecutarProceso(Util.RutaSistema("schtasks.exe"), "/Query /TN " + NombreTarea, 20000);
            e.Tarea = r.Ok;
            if (e.Tarea)
            {
                // Qué .exe tiene programado, para avisar si la app se movió de carpeta
                List<string[]> filas = Util.PowerShellFilas(
                    "$t = Get-ScheduledTask -TaskName " + Util.ComillasPS(NombreTarea) + "\n" +
                    "if ($t) { L ($t.Actions | Select-Object -First 1).Execute }", 30000);
                if (filas.Count > 0 && filas[0].Length > 0) e.RutaTarea = filas[0][0].Trim().Trim('"');
            }

            try { e.WindowsOld = Directory.Exists(Path.Combine(Sistema.UnidadSistema, "Windows.old")); } catch { }
            try
            {
                FileInfo fi = new FileInfo(Util.ArchivoLog);
                if (fi.Exists)
                {
                    e.LogTamano = fi.Length;
                    e.LogFecha = fi.LastWriteTime;
                }
            }
            catch { }
            return e;
        }

        private static string[] LeerUltimoPunto()
        {
            const string script =
                "$r = Get-ComputerRestorePoint | Sort-Object SequenceNumber | Select-Object -Last 1\n" +
                "if ($r) { L 'si' ([Management.ManagementDateTimeConverter]::ToDateTime($r.CreationTime).ToString('yyyy-MM-dd HH:mm')) } else { L 'no' }";
            List<string[]> filas = Util.PowerShellFilas(script, 30000);
            return filas.Count > 0 ? filas[filas.Count - 1] : null;
        }

        private void Aplicar(Estados e)
        {
            bool admin = Util.EsAdmin;
            lote++;
            try
            {
                // Hibernación
                if (!ocupadoHiber)
                {
                    tamHiber = e.Hiber;
                    hiberActiva = e.Hiber >= 0;
                    if (hiberActiva)
                    {
                        tHiber.PonerDescripcion("El archivo de hibernación ocupa " + Util.FormatoTamano(e.Hiber) +
                            ". Si lo quitas desaparecen “Hibernar” y el “Inicio rápido” (Windows arrancará unos segundos más lento). " +
                            "En portátiles que hibernas a menudo, mejor no quitarlo.");
                        btnHiber.Text = "Desactivar";
                        btnHiber.Estilo = EstiloBoton.Peligro;
                        if (admin) tHiber.PonerEstado("Activada · ocupa " + Util.FormatoTamano(e.Hiber), Tema.Acento);
                        else tHiber.PonerEstado(SinAdmin, Tema.Aviso);
                    }
                    else
                    {
                        tHiber.PonerDescripcion("La hibernación está apagada, así que no ocupa espacio. " +
                            "Actívala si quieres usar “Hibernar” o el “Inicio rápido” de Windows.");
                        btnHiber.Text = "Activar";
                        btnHiber.Estilo = EstiloBoton.Secundario;
                        if (admin) tHiber.PonerEstado("Desactivada", Tema.TextoTenue);
                        else tHiber.PonerEstado(SinAdmin, Tema.Aviso);
                    }
                    btnHiber.Enabled = admin;
                }

                // Sensor de almacenamiento (es del usuario: no hace falta administrador)
                if (!ocupadoSensor)
                {
                    if (e.Sensor < 0)
                    {
                        swSensor.Enabled = false;
                        tSensor.PonerEstado("No se pudo leer el estado", Tema.TextoTenue);
                    }
                    else
                    {
                        swSensor.Establecer(e.Sensor == 1);
                        swSensor.Enabled = true;
                        if (e.Sensor == 1) tSensor.PonerEstado("Activado · " + Frecuencia(e.Frecuencia), Tema.Exito);
                        else tSensor.PonerEstado("Apagado", Tema.TextoTenue);
                    }
                }

                // Limpieza semanal
                if (!ocupadoSemanal)
                {
                    bool otraRuta = e.Tarea && !string.IsNullOrEmpty(e.RutaTarea) && !MismaRuta(e.RutaTarea, Application.ExecutablePath);
                    if (!e.Tarea)
                    {
                        if (admin) tSemanal.PonerEstado("No está programada", Tema.TextoTenue);
                        else tSemanal.PonerEstado(SinAdmin, Tema.Aviso);
                        btnProgramar.Text = "Programar";
                        btnProgramar.Visible = true;
                        btnQuitar.Visible = false;
                    }
                    else if (otraRuta)
                    {
                        tSemanal.PonerEstado("Programada con la app en otra carpeta: vuelve a programarla", Tema.Aviso);
                        btnProgramar.Text = "Volver a programar";
                        btnProgramar.Visible = true;
                        btnQuitar.Visible = true;
                    }
                    else
                    {
                        if (admin) tSemanal.PonerEstado("Programada: cada domingo a las 12:00", Tema.Exito);
                        else tSemanal.PonerEstado("Programada. Para cambiarla necesitas permisos de administrador", Tema.Aviso);
                        btnProgramar.Visible = false;
                        btnQuitar.Visible = true;
                    }
                    btnProgramar.Enabled = admin;
                    btnQuitar.Enabled = admin;
                }

                // Liberador de espacio
                if (e.WindowsOld) tLiberador.PonerEstado("Tienes Windows.old en tu disco", Tema.Aviso);
                else tLiberador.PonerEstado("No tienes Windows.old ahora mismo", Tema.TextoTenue);

                // Registro
                if (e.LogTamano >= 0) tRegistro.PonerEstado("Última anotación: " + Fecha(e.LogFecha) + " · " + Util.FormatoTamano(e.LogTamano), Tema.TextoTenue);
                else tRegistro.PonerEstado("Todavía no hay nada anotado", Tema.TextoTenue);
            }
            finally
            {
                lote--;
            }
            Reordenar();
        }

        private void AplicarPunto(string[] f)
        {
            if (ocupadoPunto) return;
            DateTime d;
            if (f != null && f.Length > 1 && f[0] == "si" &&
                DateTime.TryParseExact(f[1], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                tPunto.PonerEstado("Último punto: " + Fecha(d), Tema.TextoTenue);
            }
            else if (f != null && f.Length > 0 && f[0] == "no")
            {
                tPunto.PonerEstado("Todavía no tienes ningún punto guardado", Tema.Aviso);
            }
            else
            {
                tPunto.PonerEstado("Recomendado antes de hacer cambios grandes", Tema.TextoTenue);
            }
        }

        private static string Frecuencia(int dias)
        {
            switch (dias)
            {
                case 1: return "cada día";
                case 7: return "cada semana";
                case 30: return "cada mes";
                case 0: return "cuando queda poco espacio";
            }
            return "funciona solo";
        }

        private static string Fecha(DateTime d)
        {
            if (d.Date == DateTime.Today) return "hoy a las " + d.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (d.Date == DateTime.Today.AddDays(-1)) return "ayer a las " + d.ToString("HH:mm", CultureInfo.InvariantCulture);
            return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static bool MismaRuta(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Hay "ok" en una línea propia (lo escribe el script solo si todo fue bien).</summary>
        private static bool ContieneOk(string salida)
        {
            foreach (string l in (salida ?? "").Split('\n'))
            {
                if (l.Trim() == "ok") return true;
            }
            return false;
        }

        /// <summary>Mensaje de error del script (líneas "error  mensaje"), recortado.</summary>
        private static string MensajeError(ResultadoProceso r)
        {
            foreach (string l in (r.Salida ?? "").Split('\n'))
            {
                string t = l.Trim();
                if (t.StartsWith("error\t")) return Util.Recortar(t.Substring(6).Trim(), 220);
            }
            return Util.Recortar((r.Error ?? "").Trim(), 220);
        }

        // ------------------------------------------------------------------ 1. Punto de restauración

        private async void CrearPunto()
        {
            if (ocupadoPunto || !Util.EsAdmin) return;
            if (!Dialogo.Confirmar(FindForm(), "¿Crear un punto de restauración?",
                "Windows guardará una copia de su configuración. Tarda un minuto más o menos y puedes seguir usando el PC.",
                "Crear punto", false))
            {
                return;
            }
            ocupadoPunto = true;
            btnPunto.Enabled = false;
            tPunto.PonerBarra(true, -1);
            tPunto.PonerEstado("Creando el punto de restauración…", Tema.Acento);
            try
            {
                const string script =
                    "try {\n" +
                    "  Checkpoint-Computer -Description 'Optimizador de PC' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop\n" +
                    "  L 'ok'\n" +
                    "} catch { L 'error' $_.Exception.Message }";
                ResultadoProceso r = await Task.Run(() => Util.EjecutarPowerShell(script, 300000));
                if (IsDisposed) return;
                if (ContieneOk(r.Salida))
                {
                    Util.Log("Herramientas: punto de restauración creado");
                    Dialogo.Exito(FindForm(), "Punto de restauración creado",
                        "Si algo sale mal, podrás volver a como está ahora desde Protección del sistema. " +
                        "Si ya habías creado uno hoy, Windows puede no crear otro.");
                }
                else
                {
                    Util.Log("Herramientas: no se pudo crear el punto de restauración: " + (r.Salida + " " + r.Error).Trim());
                    Dialogo.Error(FindForm(), "No se pudo crear el punto",
                        "Puede que la Protección del sistema esté apagada. Actívala en Panel de control > Sistema > " +
                        "Protección del sistema y vuelve a intentarlo.");
                }
            }
            catch (Exception ex)
            {
                Util.Log("Herramientas: error al crear el punto de restauración: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "No se pudo crear el punto", "Algo salió mal. El detalle está en el registro de la app.");
            }
            finally
            {
                ocupadoPunto = false;
                if (!IsDisposed)
                {
                    tPunto.PonerBarra(false, 0);
                    btnPunto.Enabled = Util.EsAdmin;
                    tPunto.PonerEstado("Comprobando…", Tema.TextoTenue);
                    RefrescarEstados();
                }
            }
        }

        // ------------------------------------------------------------------ 2. Limpieza profunda (DISM)

        private async void LimpiezaProfunda()
        {
            if (ocupadoDism || !Util.EsAdmin) return;
            if (!Dialogo.Confirmar(FindForm(), "¿Empezar la limpieza profunda?",
                "Windows borrará las versiones viejas de sus archivos. Tarda entre 5 y 20 minutos y no se puede parar a medias.\n\n" +
                "No apagues ni reinicies el PC hasta que termine.",
                "Empezar", false))
            {
                return;
            }
            ocupadoDism = true;
            btnDism.Enabled = false;
            tDism.PonerBarra(true, -1);
            tDism.PonerEstado("Preparando… No apagues el PC", Tema.Acento);
            long antes = Sistema.Disco().Libre;
            Util.Log("Herramientas: empieza la limpieza profunda (DISM)");
            try
            {
                Encoding cod;
                try { cod = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { cod = Encoding.Default; }
                Regex rx = new Regex(@"(\d+[.,]\d+)\s*%");
                // Se llama desde otro hilo: la interfaz se toca con BeginInvoke
                Action<string> alLinea = delegate(string linea)
                {
                    Match m = rx.Match(linea ?? "");
                    if (!m.Success) return;
                    double pct;
                    if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out pct)) return;
                    try
                    {
                        if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(delegate { ProgresoDism(pct); }));
                    }
                    catch { }
                };
                ResultadoProceso r = await Task.Run(() => Util.EjecutarProceso(Util.RutaSistema("dism.exe"),
                    "/Online /Cleanup-Image /StartComponentCleanup", 3600000, cod, alLinea));
                if (IsDisposed) return;
                long ganado = Math.Max(0, Sistema.Disco().Libre - antes);
                Util.Log(string.Format("Herramientas: limpieza profunda terminada (código {0}), espacio ganado {1}", r.Codigo, Util.FormatoTamano(ganado)));
                Historial.Anotar("Herramientas", "Limpieza profunda de Windows", ganado);
                if (r.Codigo == 0 || r.Codigo == 3010)
                {
                    string msg = ganado >= 50 * 1024L * 1024
                        ? "Has ganado " + Util.FormatoTamano(ganado) + " de espacio en el disco."
                        : "Windows ya estaba bastante limpio: no había mucho que borrar.";
                    if (r.Codigo == 3010) msg += " Reinicia el PC para terminar.";
                    Dialogo.Exito(FindForm(), "Limpieza profunda terminada", msg);
                }
                else
                {
                    string detalle = LineaErrorDism(r);
                    Dialogo.Error(FindForm(), "La limpieza no se pudo completar",
                        "Windows terminó con el código " + r.Codigo + "." + (detalle.Length > 0 ? "\n\n" + detalle : "") +
                        "\n\nPuedes intentarlo otra vez más tarde.");
                }
            }
            catch (Exception ex)
            {
                Util.Log("Herramientas: error en la limpieza profunda: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "La limpieza no se pudo completar", "Algo salió mal. El detalle está en el registro de la app.");
            }
            finally
            {
                ocupadoDism = false;
                if (!IsDisposed)
                {
                    tDism.PonerBarra(false, 0);
                    btnDism.Enabled = Util.EsAdmin;
                    tDism.PonerEstado(Util.EsAdmin ? "Lista para empezar" : SinAdmin, Util.EsAdmin ? Tema.TextoTenue : Tema.Aviso);
                }
            }
        }

        private void ProgresoDism(double pct)
        {
            if (IsDisposed || !ocupadoDism) return;
            tDism.PonerBarra(true, pct);
            tDism.PonerEstado("Limpiando… " + pct.ToString("N1", Es) + " % · No apagues el PC", Tema.Acento);
        }

        private static string LineaErrorDism(ResultadoProceso r)
        {
            string ultima = "";
            foreach (string l in ((r.Salida ?? "") + "\n" + (r.Error ?? "")).Split('\n'))
            {
                string t = l.Trim();
                if (t.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || t.IndexOf("0x8", StringComparison.OrdinalIgnoreCase) >= 0) ultima = t;
            }
            return Util.Recortar(ultima, 200);
        }

        // ------------------------------------------------------------------ 3. Hibernación

        private async void CambiarHibernacion()
        {
            if (ocupadoHiber || !Util.EsAdmin) return;
            bool desactivar = hiberActiva;
            if (desactivar)
            {
                if (!Dialogo.Confirmar(FindForm(), "¿Desactivar la hibernación?",
                    "Liberarás " + (tamHiber > 0 ? Util.FormatoTamano(tamHiber) : "espacio") + ". Desaparecerán “Hibernar” y el “Inicio rápido”, " +
                    "y Windows arrancará unos segundos más lento. Puedes volver a activarla aquí cuando quieras.",
                    "Desactivar", true))
                {
                    return;
                }
            }
            else if (!Dialogo.Confirmar(FindForm(), "¿Activar la hibernación?",
                "Windows volverá a crear el archivo de hibernación, que ocupa varios GB en el disco " + Sistema.LetraSistema + ".",
                "Activar", false))
            {
                return;
            }

            ocupadoHiber = true;
            btnHiber.Enabled = false;
            tHiber.PonerEstado(desactivar ? "Desactivando…" : "Activando…", Tema.Acento);
            try
            {
                ResultadoProceso r = await Task.Run(() => Util.EjecutarProceso(Util.RutaSistema("powercfg.exe"),
                    desactivar ? "/hibernate off" : "/hibernate on", 60000));
                if (IsDisposed) return;
                if (r.Ok)
                {
                    Util.Log("Herramientas: hibernación " + (desactivar ? "desactivada" : "activada"));
                    if (desactivar) Historial.Anotar("Herramientas", "Desactivar la hibernación", tamHiber);
                    if (desactivar && tamHiber > 0)
                        Dialogo.Exito(FindForm(), "Hibernación desactivada", "Has liberado " + Util.FormatoTamano(tamHiber) + " en el disco " + Sistema.LetraSistema + ".");
                    else if (!desactivar)
                        Dialogo.Exito(FindForm(), "Hibernación activada", "Ya puedes usar “Hibernar” y el “Inicio rápido” de Windows.");
                }
                else
                {
                    Util.Log("Herramientas: powercfg /hibernate falló (" + r.Codigo + "): " + (r.Salida + " " + r.Error).Trim());
                    string detalle = Util.Recortar((r.Salida + " " + r.Error).Trim(), 220);
                    Dialogo.Error(FindForm(), "No se pudo cambiar la hibernación",
                        "Windows no lo ha permitido." + (detalle.Length > 0 ? "\n\n" + detalle : ""));
                }
            }
            catch (Exception ex)
            {
                Util.Log("Herramientas: error al cambiar la hibernación: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "No se pudo cambiar la hibernación", "Algo salió mal. El detalle está en el registro de la app.");
            }
            finally
            {
                ocupadoHiber = false;
                if (!IsDisposed) RefrescarEstados();
            }
        }

        // ------------------------------------------------------------------ 4. Sensor de almacenamiento

        private async void SensorCambiado(object sender, EventArgs e)
        {
            if (ocupadoSensor) return;
            bool activar = swSensor.Activado;
            if (activar && !Dialogo.Confirmar(FindForm(), "¿Activar el Sensor de almacenamiento?",
                "Windows borrará solo, cada semana, los archivos temporales y lo que lleve más de 30 días en la papelera. " +
                "Puedes apagarlo aquí cuando quieras.",
                "Activar", false))
            {
                swSensor.Establecer(false);
                return;
            }
            ocupadoSensor = true;
            swSensor.Enabled = false;
            string error = null;
            try
            {
                await Task.Run(() => EscribirSensor(activar));
                Util.Log("Herramientas: Sensor de almacenamiento " + (activar ? "activado (semanal, temporales y papelera de más de 30 días)" : "apagado"));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Util.Log("Herramientas: no se pudo cambiar el Sensor de almacenamiento: " + ex);
            }
            ocupadoSensor = false;
            if (IsDisposed) return;
            swSensor.Enabled = true;
            if (error != null)
            {
                swSensor.Establecer(!activar);
                Dialogo.Error(FindForm(), "No se pudo cambiar el Sensor de almacenamiento",
                    "Prueba a hacerlo desde Configuración > Sistema > Almacenamiento.");
            }
            RefrescarEstados();
        }

        private static void EscribirSensor(bool activar)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClaveSensor))
            {
                if (k == null) throw new InvalidOperationException("No se pudo abrir el registro.");
                if (activar)
                {
                    k.SetValue("01", 1, RegistryValueKind.DWord);
                    k.SetValue("2048", 7, RegistryValueKind.DWord);
                    k.SetValue("04", 1, RegistryValueKind.DWord);
                    k.SetValue("08", 1, RegistryValueKind.DWord);
                    k.SetValue("256", 30, RegistryValueKind.DWord);
                }
                else
                {
                    k.SetValue("01", 0, RegistryValueKind.DWord);
                }
            }
        }

        // ------------------------------------------------------------------ 5. Limpieza semanal

        private async void ProgramarTarea()
        {
            if (ocupadoSemanal || !Util.EsAdmin) return;
            string exe = Application.ExecutablePath;
            if (!Dialogo.Confirmar(FindForm(), "¿Programar la limpieza semanal?",
                "Cada domingo a las 12:00 el optimizador borrará solo lo recomendado: archivos temporales, restos de Windows Update, " +
                "informes de errores y miniaturas. No toca tus documentos ni la papelera.",
                "Programar", false))
            {
                return;
            }
            ocupadoSemanal = true;
            btnProgramar.Enabled = false;
            btnQuitar.Enabled = false;
            tSemanal.PonerEstado("Programando…", Tema.Acento);
            try
            {
                string script =
                    "try {\n" +
                    "  $a = New-ScheduledTaskAction -Execute " + Util.ComillasPS(exe) + " -Argument '/limpiezarapida'\n" +
                    "  $t = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At '12:00'\n" +
                    "  $s = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)\n" +
                    "  $p = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -RunLevel Highest -LogonType Interactive\n" +
                    "  Register-ScheduledTask -TaskName " + Util.ComillasPS(NombreTarea) + " -Action $a -Trigger $t -Settings $s -Principal $p " +
                    "-Description 'Limpieza semanal del Optimizador de PC' -Force -ErrorAction Stop | Out-Null\n" +
                    "  L 'ok'\n" +
                    "} catch { L 'error' $_.Exception.Message }";
                ResultadoProceso r = await Task.Run(() => Util.EjecutarPowerShell(script, 60000));
                if (IsDisposed) return;
                if (ContieneOk(r.Salida))
                {
                    Util.Log("Herramientas: limpieza semanal programada (" + exe + ")");
                    Dialogo.Exito(FindForm(), "Limpieza semanal programada",
                        "Se hará cada domingo a las 12:00. Si el PC está apagado a esa hora, se hará la próxima vez que lo enciendas.");
                }
                else
                {
                    string detalle = MensajeError(r);
                    Util.Log("Herramientas: no se pudo programar la limpieza semanal: " + detalle);
                    Dialogo.Error(FindForm(), "No se pudo programar la limpieza",
                        "Windows no ha dejado crear la tarea." + (detalle.Length > 0 ? "\n\n" + detalle : ""));
                }
            }
            catch (Exception ex)
            {
                Util.Log("Herramientas: error al programar la limpieza semanal: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "No se pudo programar la limpieza", "Algo salió mal. El detalle está en el registro de la app.");
            }
            finally
            {
                ocupadoSemanal = false;
                if (!IsDisposed) RefrescarEstados();
            }
        }

        private async void QuitarTarea()
        {
            if (ocupadoSemanal || !Util.EsAdmin) return;
            if (!Dialogo.Confirmar(FindForm(), "¿Quitar la limpieza semanal?",
                "El optimizador dejará de limpiar solo cada domingo. Puedes volver a programarla cuando quieras.",
                "Quitar", false))
            {
                return;
            }
            ocupadoSemanal = true;
            btnProgramar.Enabled = false;
            btnQuitar.Enabled = false;
            tSemanal.PonerEstado("Quitando…", Tema.Acento);
            try
            {
                string script =
                    "try {\n" +
                    "  Unregister-ScheduledTask -TaskName " + Util.ComillasPS(NombreTarea) + " -Confirm:$false -ErrorAction Stop\n" +
                    "  L 'ok'\n" +
                    "} catch { L 'error' $_.Exception.Message }";
                ResultadoProceso r = await Task.Run(() => Util.EjecutarPowerShell(script, 60000));
                if (IsDisposed) return;
                if (ContieneOk(r.Salida))
                {
                    Util.Log("Herramientas: limpieza semanal quitada");
                }
                else
                {
                    string detalle = MensajeError(r);
                    Util.Log("Herramientas: no se pudo quitar la limpieza semanal: " + detalle);
                    Dialogo.Error(FindForm(), "No se pudo quitar la limpieza",
                        "Windows no ha dejado borrar la tarea." + (detalle.Length > 0 ? "\n\n" + detalle : ""));
                }
            }
            catch (Exception ex)
            {
                Util.Log("Herramientas: error al quitar la limpieza semanal: " + ex);
                if (!IsDisposed) Dialogo.Error(FindForm(), "No se pudo quitar la limpieza", "Algo salió mal. El detalle está en el registro de la app.");
            }
            finally
            {
                ocupadoSemanal = false;
                if (!IsDisposed) RefrescarEstados();
            }
        }

        // ------------------------------------------------------------------ 6 y 7. Herramientas de Windows y registro

        private void AbrirLiberador()
        {
            if (!Util.Abrir(Util.RutaSistema("cleanmgr.exe"), "/d " + Sistema.LetraSistema.TrimEnd(':')))
            {
                Dialogo.Error(FindForm(), "No se pudo abrir el Liberador", "Búscalo en el menú Inicio como “Liberador de espacio en disco”.");
                return;
            }
            Util.Log("Herramientas: abierto el Liberador de espacio de Windows");
            Dialogo.Info(FindForm(), "Se está abriendo el Liberador",
                "Cuando termine de calcular, pulsa “Limpiar archivos del sistema”. Después marca “Instalaciones anteriores de Windows” " +
                "(si aparece) y pulsa Aceptar.");
        }

        private void AbrirRegistro()
        {
            string ruta = Util.ArchivoLog;
            if (!File.Exists(ruta))
            {
                Dialogo.Info(FindForm(), "Todavía no hay nada anotado", "Cuando el optimizador haga algo, lo verás aquí.");
                return;
            }
            if (!Util.Abrir(Util.RutaSistema("notepad.exe"), "\"" + ruta + "\""))
            {
                Dialogo.Error(FindForm(), "No se pudo abrir el registro", "El archivo está en " + ruta);
            }
        }

        // ------------------------------------------------------------------ tarjeta

        /// <summary>Tarjeta de una herramienta: icono, título, descripción, estado, notas y botones abajo.</summary>
        private class TarjetaHerramienta : Tarjeta
        {
            private const TextFormatFlags Envolver = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                                                     TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
            private const TextFormatFlags Linea = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                                  TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            private class Disposicion
            {
                public Rectangle Icono, Titulo, Descripcion, Estado, Ruta, Nota, Barra;
                public int AltoBotones, Alto;
            }

            private readonly string glifo, titulo;
            private string descripcion, estado = "", nota = "", ruta = "";
            private Color colorEstado = Tema.TextoTenue;
            private bool conBarra;
            private Control extra;
            private int ultimoAlto = -1;

            public readonly FlowLayoutPanel Botones;
            public readonly BarraUso Barra;

            /// <summary>Cambió el alto que necesita la tarjeta (hay que recolocar la rejilla).</summary>
            public event EventHandler AltoCambiado;

            public TarjetaHerramienta(string glifo, string titulo, string descripcion)
            {
                this.glifo = glifo;
                this.titulo = titulo;
                this.descripcion = descripcion ?? "";
                Padding = new Padding(Tema.S(22));

                Botones = new FlowLayoutPanel();
                Botones.FlowDirection = FlowDirection.LeftToRight;
                Botones.WrapContents = false;
                Botones.AutoSize = true;
                Botones.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Botones.Margin = new Padding(0);
                Botones.Padding = new Padding(0);

                Barra = new BarraUso();
                Barra.Height = Tema.S(6);
                Barra.Visible = false;

                Controls.Add(Botones);
                Controls.Add(Barra);
            }

            private static Font FTitulo { get { return Tema.FuenteSemibold(11f); } }
            private static Font FDesc { get { return Tema.Fuente(9.5f); } }
            private static Font FEstado { get { return Tema.Fuente(9f); } }
            private static Font FNota { get { return Tema.Fuente(8.5f); } }

            public void PonerDescripcion(string texto) { descripcion = texto ?? ""; Cambio(); }

            public void PonerEstado(string texto, Color color)
            {
                estado = texto ?? "";
                colorEstado = color;
                Cambio();
            }

            /// <summary>Ruta (se recorta por el medio) y una nota pequeña debajo del estado.</summary>
            public void PonerNotas(string rutaArchivo, string texto)
            {
                ruta = rutaArchivo ?? "";
                nota = texto ?? "";
                Cambio();
            }

            /// <summary>Control a la derecha del título (por ejemplo, un interruptor).</summary>
            public void PonerExtra(Control c)
            {
                extra = c;
                Controls.Add(c);
                Cambio();
            }

            /// <summary>Barra de progreso: valor de 0 a 100, o negativo para animación sin porcentaje.</summary>
            public void PonerBarra(bool visible, double valor)
            {
                conBarra = visible;
                if (visible)
                {
                    if (valor < 0)
                    {
                        if (!Barra.Indeterminado) Barra.Indeterminado = true;
                    }
                    else
                    {
                        if (Barra.Indeterminado) Barra.Indeterminado = false;
                        Barra.Valor = valor;
                    }
                }
                else if (Barra.Indeterminado)
                {
                    Barra.Indeterminado = false;
                }
                Cambio();
            }

            public int AltoNecesario(int ancho) { return Calcular(ancho).Alto; }

            private void Cambio()
            {
                Colocar();
                int alto = Calcular(Width).Alto;
                if (alto != ultimoAlto)
                {
                    ultimoAlto = alto;
                    if (AltoCambiado != null) AltoCambiado(this, EventArgs.Empty);
                }
            }

            private static int AltoTexto(string t, Font f, int ancho)
            {
                if (string.IsNullOrEmpty(t)) return 0;
                return TextRenderer.MeasureText(t, f, new Size(Math.Max(1, ancho), int.MaxValue), Envolver).Height;
            }

            private static int AltoLinea(Font f)
            {
                return TextRenderer.MeasureText("Ág", f, new Size(int.MaxValue, int.MaxValue), Linea).Height;
            }

            private Disposicion Calcular(int ancho)
            {
                Disposicion d = new Disposicion();
                int p = Padding.Left, arriba = Padding.Top;
                int w = Math.Max(Tema.S(40), ancho - Padding.Horizontal);
                int lado = Tema.S(40);
                d.Icono = new Rectangle(p, arriba, lado, lado);
                int xt = p + lado + Tema.S(14);
                int reservado = extra != null ? extra.Width + Tema.S(12) : 0;
                int ht = AltoLinea(FTitulo);
                d.Titulo = new Rectangle(xt, arriba + (lado - ht) / 2, Math.Max(Tema.S(20), ancho - Padding.Right - xt - reservado), ht);

                int y = arriba + lado + Tema.S(14);
                int h = AltoTexto(descripcion, FDesc, w);
                d.Descripcion = new Rectangle(p, y, w, h);
                y += h;
                if (estado.Length > 0)
                {
                    y += Tema.S(12);
                    int sangria = Tema.S(16);
                    h = AltoTexto(estado, FEstado, w - sangria);
                    d.Estado = new Rectangle(p + sangria, y, w - sangria, h);
                    y += h;
                }
                if (ruta.Length > 0)
                {
                    y += Tema.S(8);
                    h = AltoLinea(FNota);
                    d.Ruta = new Rectangle(p, y, w, h);
                    y += h;
                }
                if (nota.Length > 0)
                {
                    y += Tema.S(ruta.Length > 0 ? 2 : 8);
                    h = AltoTexto(nota, FNota, w);
                    d.Nota = new Rectangle(p, y, w, h);
                    y += h;
                }
                if (conBarra)
                {
                    y += Tema.S(14);
                    d.Barra = new Rectangle(p, y, w, Barra.Height);
                    y += Barra.Height;
                }
                y += Tema.S(20);
                d.AltoBotones = Math.Max(Tema.S(36), Botones.GetPreferredSize(Size.Empty).Height);
                d.Alto = y + d.AltoBotones + Padding.Bottom;
                return d;
            }

            private void Colocar()
            {
                Disposicion d = Calcular(Width);
                // Los botones siempre abajo, así quedan alineados con los de la tarjeta de al lado
                Botones.Location = new Point(Padding.Left, Height - Padding.Bottom - d.AltoBotones);
                if (conBarra) Barra.Bounds = d.Barra;
                Barra.Visible = conBarra;
                if (extra != null)
                {
                    extra.Location = new Point(Width - Padding.Right - extra.Width, d.Icono.Y + (d.Icono.Height - extra.Height) / 2);
                }
                Invalidate();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                Colocar();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Disposicion d = Calcular(Width);

                using (GraphicsPath gp = Tema.Redondeado(d.Icono, Tema.S(10)))
                using (SolidBrush b = new SolidBrush(Tema.Seleccion))
                {
                    g.FillPath(b, gp);
                }
                TextRenderer.DrawText(g, glifo, Tema.FuenteIconos(14f), d.Icono, Tema.Acento,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, titulo, FTitulo, d.Titulo, Tema.Texto, Linea);
                TextRenderer.DrawText(g, descripcion, FDesc, d.Descripcion, Tema.TextoSuave, Envolver);

                if (estado.Length > 0)
                {
                    int lado = Tema.S(8);
                    int hl = AltoLinea(FEstado);
                    Rectangle punto = new Rectangle(Padding.Left + Tema.S(1), d.Estado.Y + (hl - lado) / 2, lado, lado);
                    using (SolidBrush b = new SolidBrush(colorEstado)) g.FillEllipse(b, punto);
                    TextRenderer.DrawText(g, estado, FEstado, d.Estado, colorEstado, Envolver);
                }
                if (ruta.Length > 0)
                {
                    TextRenderer.DrawText(g, ruta, FNota, d.Ruta, Tema.TextoTenue,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PathEllipsis);
                }
                if (nota.Length > 0) TextRenderer.DrawText(g, nota, FNota, d.Nota, Tema.TextoTenue, Envolver);
            }
        }
    }
}
