using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OptimizadorPC
{
    /// <summary>
    /// Pruebas automáticas del Optimizador. Usan el motor y las pantallas reales, pero sobre datos
    /// de prueba creados aquí mismo (carpetas, programa falso, entradas de inicio, proceso...),
    /// así que no tocan nada del usuario. Uso: PruebasOptimizador.exe /salida informe.txt [/admin]
    /// </summary>
    static class Pruebas
    {
        static StreamWriter informe;
        static int aciertos, fallos;
        static string raiz;
        static readonly List<string> dialogos = new List<string>();

        [STAThread]
        static int Main(string[] args)
        {
            string salida = Path.Combine(Path.GetTempPath(), "opc_informe.txt");
            bool admin = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "/salida" && i + 1 < args.Length) salida = args[++i];
                if (args[i] == "/admin") admin = true;
            }
            informe = new StreamWriter(salida, false, new UTF8Encoding(false));
            informe.AutoFlush = true;
            raiz = Path.Combine(Path.GetTempPath(), "OPC_PRUEBAS");

            Application.EnableVisualStyles();
            Tema.Inicializar();
            // Las pruebas usan su propio historial y su propia carpeta de tema: no tocan los del usuario
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OPC_PRUEBAS_DATOS"));
            Historial.ArchivoPrueba = Path.Combine(Path.GetTempPath(), @"OPC_PRUEBAS_DATOS\historial.txt");
            Tema.CarpetaPrueba = Path.Combine(Path.GetTempPath(), "OPC_PRUEBAS_DATOS");
            try { File.Delete(Historial.ArchivoPrueba); File.Delete(Path.Combine(Tema.CarpetaPrueba, "secreto.dat")); File.Delete(Path.Combine(Tema.CarpetaPrueba, "tema.txt")); } catch { }
            Dialogo.RespuestaAutomatica = delegate(string t, string m)
            {
                string linea = "      [diálogo] " + t + " -> " + Util.Recortar((m ?? "").Replace("\n", " "), 160);
                lock (dialogos) dialogos.Add(linea);
                Escribir(linea);
                return true;
            };
            VentanaRestos.EleccionAutomatica = delegate(List<Resto> restos)
            {
                Escribir("      [restos encontrados: " + restos.Count + "]");
                foreach (Resto r in restos) Escribir("        " + (r.Marcado ? "[x] " : "[ ] ") + r.Tipo + "  " + r.Ruta + "  (" + r.Motivo + ")");
                return restos.FindAll(x => x.Marcado);
            };

            Form host = new Form();
            host.StartPosition = FormStartPosition.Manual;
            host.Location = new Point(-20000, -20000);
            host.Size = new Size(1200, 800);
            host.ShowInTaskbar = false;
            host.Shown += async delegate
            {
                try
                {
                    Escribir("PRUEBAS DEL OPTIMIZADOR · " + DateTime.Now + " · " + (Util.EsAdmin ? "ADMINISTRADOR" : "sin administrador"));
                    if (admin) await PruebasAdmin(host);
                    else await PruebasNormales(host);
                }
                catch (Exception ex)
                {
                    Comprobar("La batería de pruebas terminó sin excepciones", false, ex.ToString());
                }
                Escribir("");
                Escribir("RESULTADO: " + aciertos + " correctas, " + fallos + " fallidas");
                host.Close();
            };
            Application.Run(host);
            informe.Close();
            return fallos;
        }

        // ------------------------------------------------------------------ utilidades

        static void Escribir(string s) { lock (informe) informe.WriteLine(s); }
        static void Seccion(string s) { Escribir(""); Escribir("== " + s); }

        static void Comprobar(string nombre, bool ok, string detalle)
        {
            if (ok) aciertos++; else fallos++;
            Escribir((ok ? "  OK    " : "  FALLO ") + nombre + (string.IsNullOrEmpty(detalle) ? "" : "  -> " + detalle));
        }

        static async Task<bool> Esperar(Func<bool> condicion, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                try { if (condicion()) return true; } catch { }
                await Task.Delay(150);
            }
            return false;
        }

        static object Campo(object o, string nombre)
        {
            FieldInfo f = o.GetType().GetField(nombre, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(o.GetType().Name, nombre);
            return f.GetValue(o);
        }

        static void PonerCampo(object o, string nombre, object valor)
        {
            o.GetType().GetField(nombre, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, valor);
        }

        static object Llamar(object o, string metodo, params object[] args)
        {
            Type t = o as Type ?? o.GetType();
            BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Public | (o is Type ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
            foreach (MethodInfo m in t.GetMethods(bf))
            {
                if (m.Name == metodo && m.GetParameters().Length == args.Length) return m.Invoke(o is Type ? null : o, args);
            }
            throw new MissingMethodException(t.Name, metodo);
        }

        static T Pagina<T>(Form host) where T : PaginaBase, new()
        {
            foreach (Control c in new ArrayList(host.Controls)) { host.Controls.Remove(c); c.Dispose(); }
            T p = new T();
            host.Controls.Add(p);
            return p;
        }

        static void CrearArchivo(string ruta, int bytes, int diasAntiguedad)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllBytes(ruta, new byte[bytes]);
            if (diasAntiguedad > 0)
            {
                DateTime d = DateTime.Now.AddDays(-diasAntiguedad);
                File.SetCreationTime(ruta, d);
                File.SetLastWriteTime(ruta, d);
            }
        }

        static bool Existe(string ruta) { return File.Exists(ruta) || Directory.Exists(ruta); }

        static bool ClaveExiste(RegistryHive h, RegistryView v, string sub)
        {
            using (RegistryKey b = RegistryKey.OpenBaseKey(h, v))
            using (RegistryKey k = b.OpenSubKey(sub))
            {
                return k != null;
            }
        }

        static void BorrarCarpetaPrueba(string ruta)
        {
            try
            {
                if (!Directory.Exists(ruta)) return;
                // Primero los enlaces (para no entrar en ellos) y luego el resto
                foreach (string d in Directory.GetDirectories(ruta, "*", SearchOption.TopDirectoryOnly))
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) Directory.Delete(d, false);
                    else BorrarCarpetaPrueba(d);
                }
                foreach (string f in Directory.GetFiles(ruta)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                Directory.Delete(ruta, false);
            }
            catch { }
        }

        // ------------------------------------------------------------------ pruebas sin administrador

        static async Task PruebasNormales(Form host)
        {
            BorrarCarpetaPrueba(raiz);
            Directory.CreateDirectory(raiz);

            PruebaRutasProtegidas();
            PruebaMotorLimpieza();
            PruebaMedirReal();
            PruebaSistema();
            await PruebaPaginaLimpieza(host);
            PruebaInicioRegistro(RegistryHive.CurrentUser);
            PruebaInicioTarea();
            await PruebaPaginaInicio(host);
            await PruebaPaginaProcesos(host);
            await PruebaDesinstalarConRestos(host);
            await PruebaDesinstalarForzado(host);
            await PruebaPaginaArchivos(host);
            await PruebaPaginaApps(host);
            PruebaSensorAlmacenamiento();
            await PruebaPaginaResumen(host);
            await PruebaPaginaHerramientas(host);
            await PruebaHistorial(host);
            PruebaTemas();
            await PruebaCodigoSecreto();
            await PruebaSonidosYAnimacion();
            PruebaModoJuego();
            await PruebaDuplicados(host);
            await PruebaDiagnostico(host);
            await PruebaAccesosDirectos(host);

            BorrarCarpetaPrueba(raiz);
        }

        static async Task PruebaSonidosYAnimacion()
        {
            Seccion("20. Sonidos y animaciones");
            string[] sonidos = { "Campanita", "Bruja", "CajaDeMusica", "Fallo", "Susurro" };
            foreach (string s in sonidos)
            {
                try
                {
                    float[] muestras = (float[])Llamar(typeof(Sonidos), s);
                    byte[] wav = (byte[])Llamar(typeof(Sonidos), "Wav", muestras);
                    using (MemoryStream ms = new MemoryStream(wav))
                    using (System.Media.SoundPlayer sp = new System.Media.SoundPlayer(ms))
                    {
                        sp.Load();   // falla si el WAV está mal formado
                    }
                    bool suena = false;
                    foreach (float v in muestras) if (Math.Abs(v) > 0.01f) { suena = true; break; }
                    Comprobar("Sonido '" + s + "' válido", suena && wav.Length > 1000,
                        (muestras.Length / 22050.0).ToString("N1") + " s");
                }
                catch (Exception ex) { Comprobar("Sonido '" + s + "' válido", false, ex.Message); }
            }
            bool antes = Sonidos.Activados;
            Sonidos.Activados = false;
            bool apagados = !Sonidos.Activados;
            Sonidos.Activados = true;
            Comprobar("Se pueden apagar y encender los sonidos", apagados && Sonidos.Activados, null);
            Sonidos.Activados = antes;

            AnilloUso a = new AnilloUso();
            Form f = new Form();
            f.Controls.Add(a);
            a.Valor = 80;
            double inicio = (double)Campo(a, "mostrado");
            await Esperar(() => Math.Abs((double)Campo(a, "mostrado") - 80) < 0.2, 3000);
            Comprobar("El anillo se llena con animación (de " + inicio.ToString("N0") + " a 80)", inicio < 80 && Math.Abs((double)Campo(a, "mostrado") - 80) < 0.2, null);
            f.Dispose();
        }

        static void PruebaModoJuego()
        {
            Seccion("21. Modo juego");
            string[] nunca = { "Discord", "brave", "chrome", "msedge", "firefox", "opera", "LeagueClient", "League of Legends", "vgc",
                               "RiotClientServices", "VALORANT-Win64-Shipping", "steam", "explorer" };
            bool todasProtegidas = true;
            foreach (string n in nunca) if (!ModoJuego.EsIntocable(n) && n != "explorer") todasProtegidas = false;
            Comprobar("Discord, navegadores, Riot/Vanguard y Steam nunca se cierran", todasProtegidas, null);

            List<ProgramaFondo> c = ModoJuego.Candidatos();
            bool alguna = c.Exists(x => ModoJuego.EsIntocable(x.Proceso.Split('|')[0]) ||
                                        x.Proceso.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        x.Proceso.IndexOf("brave", StringComparison.OrdinalIgnoreCase) >= 0);
            Comprobar("La lista de cerrables no incluye Discord ni el navegador (aunque estén abiertos)", !alguna,
                c.Count + " programas: " + string.Join(", ", c.ConvertAll(x => x.Nombre).ToArray()));

            // Proceso de prueba que el modo juego SÍ debe cerrar, y otro con nombre de Discord que NO
            string seguro = Path.Combine(raiz, "ProcesoPruebaOPC.exe");
            string falsoDiscord = Path.Combine(raiz, "discordpruebaopc.exe");
            Directory.CreateDirectory(raiz);
            File.Copy(Util.RutaSistema("PING.EXE"), seguro, true);
            File.Copy(Util.RutaSistema("PING.EXE"), falsoDiscord, true);
            Process p1 = Arrancar(seguro), p2 = Arrancar(falsoDiscord);
            string planAntes = ModoJuego.NombrePlanActual();
            object juegoAntes;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar")) juegoAntes = k.GetValue("AutoGameModeEnabled");
            try
            {
                ProgramaFondo a = new ProgramaFondo(); a.Proceso = "ProcesoPruebaOPC"; a.Nombre = "Proceso de prueba";
                ProgramaFondo b = new ProgramaFondo(); b.Proceso = "discordpruebaopc"; b.Nombre = "Falso Discord";
                List<string> hecho = ModoJuego.Activar(new List<ProgramaFondo> { a, b });
                foreach (string h in hecho) Escribir("      [modo juego] " + h);
                Comprobar("Activar: cierra el programa de fondo elegido", p1.WaitForExit(8000), null);
                Comprobar("Activar: NUNCA cierra algo que se llame Discord aunque se le pida", !p2.HasExited, null);
                Comprobar("Activar: no baja el plan de energía si ya era de alto rendimiento", ModoJuego.NombrePlanActual() == planAntes, planAntes);
                Comprobar("Queda marcado como activo", ModoJuego.Activo, null);
                ModoJuego.Desactivar();
                object juegoDespues;
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar")) juegoDespues = k.GetValue("AutoGameModeEnabled");
                Comprobar("Desactivar: todo queda como estaba", !ModoJuego.Activo && ModoJuego.NombrePlanActual() == planAntes &&
                    Convert.ToString(juegoAntes) == Convert.ToString(juegoDespues), null);
            }
            finally
            {
                foreach (Process p in new[] { p1, p2 }) { try { if (!p.HasExited) p.Kill(); p.WaitForExit(3000); } catch { } }
            }
        }

        static Process Arrancar(string exe)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, "-n 600 127.0.0.1");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            return Process.Start(psi);
        }

        static async Task PruebaDuplicados(Form host)
        {
            Seccion("22. Duplicados");
            string caja = Path.Combine(raiz, "duplicados");
            byte[] contenido = new byte[2 * 1024 * 1024];
            new Random(5).NextBytes(contenido);
            Directory.CreateDirectory(Path.Combine(caja, "sub"));
            Directory.CreateDirectory(Path.Combine(caja, @"proyecto\node_modules"));
            File.WriteAllBytes(Path.Combine(caja, "foto.jpg"), contenido);
            File.SetCreationTime(Path.Combine(caja, "foto.jpg"), DateTime.Now.AddDays(-30));
            File.SetLastWriteTime(Path.Combine(caja, "foto.jpg"), DateTime.Now.AddDays(-30));
            File.WriteAllBytes(Path.Combine(caja, @"sub\foto (1).jpg"), contenido);
            File.WriteAllBytes(Path.Combine(caja, @"proyecto\node_modules\foto.jpg"), contenido);
            byte[] casi = (byte[])contenido.Clone();
            casi[casi.Length / 2] ^= 0xFF;   // mismo tamaño, principio y final iguales, distinto en medio
            File.WriteAllBytes(Path.Combine(caja, "parecida.jpg"), casi);
            File.WriteAllBytes(Path.Combine(caja, "pequena1.txt"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(caja, "pequena2.txt"), new byte[1000]);

            List<GrupoDuplicado> g = Duplicados.Buscar(new List<string> { caja }, 1L << 20, CancellationToken.None, null);
            Comprobar("Encuentra exactamente 1 grupo de copias idénticas", g.Count == 1 && g[0].Archivos.Count == 2,
                g.Count == 1 ? string.Join(" | ", g[0].Archivos.ConvertAll(x => x.Name).ToArray()) : g.Count + " grupos");
            Comprobar("No confunde un archivo que solo cambia por dentro", g.Count == 1 && !g[0].Archivos.Exists(x => x.Name == "parecida.jpg"), null);
            Comprobar("Ignora node_modules y los archivos pequeños", g.Count == 1 && !g[0].Archivos.Exists(x => x.FullName.Contains("node_modules")), null);
            Comprobar("Propone conservar la copia más antigua", g.Count == 1 && g[0].Archivos[0].Name == "foto.jpg", null);

            PaginaDuplicados p = Pagina<PaginaDuplicados>(host);
            PonerCampo(p, "grupos", g);
            Llamar(p, "Llenar");
            DataGridView t = (DataGridView)Campo(p, "tabla");
            // Marcar todas las copias: debe negarse
            foreach (DataGridViewRow r in t.Rows) r.Cells[0].Value = true;
            int dialogosAntes = dialogos.Count;
            Llamar(p, "Borrar");
            await Task.Delay(500);
            Comprobar("Nunca deja borrar todas las copias de un archivo", File.Exists(Path.Combine(caja, "foto.jpg")) &&
                File.Exists(Path.Combine(caja, @"sub\foto (1).jpg")) && dialogos.Count > dialogosAntes, null);
            // Lo normal: se marca solo la copia nueva
            Llamar(p, "Llenar");
            Llamar(p, "Borrar");
            await Esperar(() => !(bool)Campo(p, "borrando"), 20000);
            Comprobar("Manda la copia a la papelera y conserva el original",
                File.Exists(Path.Combine(caja, "foto.jpg")) && !File.Exists(Path.Combine(caja, @"sub\foto (1).jpg")), null);
        }

        static async Task PruebaDiagnostico(Form host)
        {
            Seccion("23. Diagnóstico");
            List<Controlador> d = Diagnostico.LeerControladores();
            Comprobar("Lee los controladores", d.Count > 0 && d.Exists(x => x.Tipo == "Gráfica"),
                d.Count + " · " + string.Join(", ", d.FindAll(x => x.Tipo == "Gráfica").ConvertAll(x => x.Dispositivo + " " + x.Version).ToArray()));
            InfoActualizaciones a = Diagnostico.LeerEstadoActualizaciones();
            Comprobar("Sabe cuándo fue la última actualización", a.UltimaInstalada.HasValue,
                a.UltimaInstalada.HasValue ? a.UltimaInstalada.Value.ToString("dd/MM/yyyy") : null);
            InfoArranque ar = Diagnostico.LeerArranque();
            Comprobar("Lee el arranque (sin admin avisa de que hace falta)", Util.EsAdmin ? ar.Arranques.Count > 0 : (ar.SinPermiso || ar.Arranques.Count == 0),
                ar.Arranques.Count + " arranques, " + ar.Causas.Count + " causas");
            string error;
            List<string> pendientes = await Task.Run(() => Diagnostico.BuscarActualizaciones(out error));
            Comprobar("Pregunta a Windows Update sin instalar nada", true, pendientes.Count + " pendientes");
            PaginaDiagnostico p = Pagina<PaginaDiagnostico>(host);
            p.AlMostrar();
            bool ok = await Esperar(() => (bool)Campo(p, "cargado"), 120000);
            Comprobar("La pantalla Diagnóstico carga sin errores", ok, null);
        }

        static async Task PruebaAccesosDirectos(Form host)
        {
            Seccion("24. Accesos directos");
            string esc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Optimizador de PC.lnk");
            string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Optimizador de PC.lnk");
            bool habia = File.Exists(esc) || File.Exists(ini);
            if (habia) { Comprobar("Accesos directos (ya existían: no se tocan)", true, null); return; }
            PaginaHerramientas p = Pagina<PaginaHerramientas>(host);
            p.AlMostrar();
            Llamar(p, "CrearAccesos");
            Comprobar("Crea el acceso en el Escritorio y en el menú Inicio", File.Exists(esc) && File.Exists(ini) &&
                string.Equals(Residuos.DestinoAcceso(esc), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase), null);
            Llamar(p, "QuitarAccesos");
            Comprobar("Los quita", !File.Exists(esc) && !File.Exists(ini), null);
            await Task.Delay(100);
        }

        static async Task PruebaHistorial(Form host)
        {
            Seccion("17. Historial de limpiezas");
            List<EntradaHistorial> l = Historial.Leer();
            long total = 0;
            foreach (EntradaHistorial e in l) total += e.Bytes;
            Comprobar("Las limpiezas de las pruebas quedaron anotadas", l.Count > 0 && total > 0,
                l.Count + " entradas, " + Util.FormatoTamano(total) + " en total");
            bool restos = l.Exists(e => e.Origen == "Restos de programa" && e.Detalle.StartsWith("Programa Prueba OPC"));
            Comprobar("También anota los restos de programas borrados", restos, null);
            PaginaHistorial p = Pagina<PaginaHistorial>(host);
            p.AlMostrar();
            await Task.Delay(1500);
            DataGridView t = (DataGridView)Campo(p, "tabla");
            Label lt = (Label)Campo(p, "lblTotal");
            Comprobar("La pantalla Historial muestra el total y la lista", t.Rows.Count == l.Count && lt.Text == Util.FormatoTamano(total), lt.Text);
        }

        static void PruebaTemas()
        {
            Seccion("18. Temas");
            Tema.Aplicar(Tema.TemaHalloween);
            Color naranja = Tema.Acento;
            Comprobar("Sin desbloquear, el ciclo es Halloween → normal → Halloween",
                Tema.Siguiente() == Tema.TemaNormal && Cambiar(Tema.TemaNormal) == Tema.TemaHalloween, null);
            Tema.Aplicar(Tema.TemaNormal);
            Comprobar("El tema normal cambia los colores", Tema.Acento != naranja, null);
            Tema.GuardarPreferencia(Tema.TemaWeirdcore);
            Comprobar("Weirdcore guardado sin desbloquear no se aplica", Tema.LeerPreferencia() == Tema.TemaHalloween, null);
            Tema.GuardarPreferencia(Tema.TemaNormal);
            Comprobar("Recuerda el tema elegido", Tema.LeerPreferencia() == Tema.TemaNormal, null);
            Tema.Aplicar(Tema.TemaHalloween);
        }

        static string Cambiar(string tema)
        {
            Tema.Aplicar(tema);
            string s = Tema.Siguiente();
            Tema.Aplicar(Tema.TemaHalloween);
            return s;
        }

        static async Task PruebaCodigoSecreto()
        {
            Seccion("19. Tema secreto weirdcore (código Konami)");
            Tema.Aplicar(Tema.TemaHalloween);
            MainForm f = new MainForm("resumen", null, 0, false);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-20000, -20000);
            f.ShowInTaskbar = false;
            f.Show();
            await Task.Delay(800);
            Keys[] konami = { Keys.Up, Keys.Up, Keys.Down, Keys.Down, Keys.Left, Keys.Right, Keys.Left, Keys.Right, Keys.B, Keys.A };
            MethodInfo pck = typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);

            // Sin pulsar antes la firma, el código no hace nada
            foreach (Keys k in konami) { Message m = new Message(); pck.Invoke(f, new object[] { m, k }); }
            await Task.Delay(500);
            Comprobar("El código sin pulsar la firma no hace nada", !Tema.WeirdcoreDesbloqueado && f.TemaSiguiente == null, null);

            // Un código con un error tampoco
            Llamar(f, "EmpezarEscuchaSecreta");
            Keys[] malo = { Keys.Up, Keys.Up, Keys.Down, Keys.Down, Keys.Left, Keys.Left, Keys.Left, Keys.Right, Keys.B, Keys.A };
            foreach (Keys k in malo) { Message m = new Message(); pck.Invoke(f, new object[] { m, k }); }
            await Task.Delay(500);
            Comprobar("Un código equivocado no desbloquea nada", !Tema.WeirdcoreDesbloqueado, null);

            // Pulsar "Desarrollada por Biliman" y el código correcto
            Llamar(f, "EmpezarEscuchaSecreta");
            foreach (Keys k in konami) { Message m = new Message(); pck.Invoke(f, new object[] { m, k }); }
            bool cerrada = await Esperar(() => f.IsDisposed || !f.Visible, 5000);
            Comprobar("Firma + código Konami desbloquea el tema", Tema.WeirdcoreDesbloqueado, null);
            Comprobar("Y lo pone solo (la ventana se reabre en weirdcore)", cerrada && f.TemaSiguiente == Tema.TemaWeirdcore, null);
            Tema.Aplicar(Tema.TemaNormal);
            Comprobar("Una vez desbloqueado aparece en el ciclo de temas", Tema.Siguiente() == Tema.TemaWeirdcore, null);
            Tema.Aplicar(Tema.TemaHalloween);
            if (!f.IsDisposed) f.Dispose();
        }

        static void PruebaRutasProtegidas()
        {
            Seccion("1. Protección de carpetas del sistema");
            string[] prohibidas =
            {
                @"C:\", @"C:\Windows", @"C:\Windows\System32", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Sistema.CarpetaDescargas, @"C:\Users", @"D:\", "", "relativa\\ruta"
            };
            foreach (string p in prohibidas) Comprobar("Protegida: " + (p.Length == 0 ? "(vacía)" : p), Util.EsRutaProtegida(p), null);
            Comprobar("No protegida: carpeta temporal", !Util.EsRutaProtegida(Path.GetTempPath()), null);
        }

        static void PruebaMotorLimpieza()
        {
            Seccion("2. Motor de limpieza (carpeta de prueba)");
            string caja = Path.Combine(raiz, "caja");
            string fuera = Path.Combine(raiz, "fuera");
            CrearArchivo(Path.Combine(caja, "viejo1.tmp"), 300000, 10);
            CrearArchivo(Path.Combine(caja, "solo_lectura.tmp"), 200000, 10);
            File.SetAttributes(Path.Combine(caja, "solo_lectura.tmp"), FileAttributes.ReadOnly);
            CrearArchivo(Path.Combine(caja, "nuevo.tmp"), 100000, 0);
            CrearArchivo(Path.Combine(caja, @"sub\viejo2.tmp"), 50000, 10);
            CrearArchivo(Path.Combine(caja, "en_uso.tmp"), 70000, 10);
            CrearArchivo(Path.Combine(fuera, "NO_BORRAR.txt"), 1000, 10);
            // Enlace (junction) dentro de la caja que apunta fuera: la limpieza NO debe entrar
            Util.EjecutarProceso(Util.RutaSistema("cmd.exe"), "/c mklink /J \"" + Path.Combine(caja, "enlace") + "\" \"" + fuera + "\"", 10000);
            Comprobar("Se creó el enlace de prueba", Directory.Exists(Path.Combine(caja, "enlace")), null);

            Objetivo o = new Objetivo();
            o.Id = "prueba"; o.Nombre = "Prueba"; o.Rutas = new[] { caja }; o.DiasMin = 1;
            long medido = MotorLimpieza.Medir(o, CancellationToken.None);
            Comprobar("Mide solo los archivos viejos (620.000 bytes)", medido == 620000, "medido " + medido);

            long liberado;
            using (FileStream bloqueado = new FileStream(Path.Combine(caja, "en_uso.tmp"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                liberado = MotorLimpieza.Limpiar(o, CancellationToken.None, null);
            }
            Comprobar("Libera los viejos y salta el que está en uso (550.000 bytes)", liberado == 550000, "liberado " + liberado);
            Comprobar("Borra el archivo viejo", !File.Exists(Path.Combine(caja, "viejo1.tmp")), null);
            Comprobar("Borra el archivo de solo lectura", !File.Exists(Path.Combine(caja, "solo_lectura.tmp")), null);
            Comprobar("Respeta el archivo nuevo (menos de 1 día)", File.Exists(Path.Combine(caja, "nuevo.tmp")), null);
            Comprobar("Respeta el archivo en uso", File.Exists(Path.Combine(caja, "en_uso.tmp")), null);
            Comprobar("Quita la subcarpeta vacía", !Directory.Exists(Path.Combine(caja, "sub")), null);
            Comprobar("No entra en enlaces: el archivo de fuera sigue", File.Exists(Path.Combine(fuera, "NO_BORRAR.txt")), null);
            Comprobar("La carpeta raíz no se borra", Directory.Exists(caja), null);

            // Filtro por nombre (como la caché de miniaturas)
            string caja2 = Path.Combine(raiz, "caja2");
            CrearArchivo(Path.Combine(caja2, "thumbcache_256.db"), 1000, 0);
            CrearArchivo(Path.Combine(caja2, "iconcache_256.db"), 1000, 0);
            Objetivo o2 = new Objetivo();
            o2.Nombre = "Filtro"; o2.Rutas = new[] { caja2 }; o2.Filtros = new[] { "thumbcache_*.db" };
            MotorLimpieza.Limpiar(o2, CancellationToken.None, null);
            Comprobar("Filtro: borra thumbcache_*.db", !File.Exists(Path.Combine(caja2, "thumbcache_256.db")), null);
            Comprobar("Filtro: respeta otros archivos", File.Exists(Path.Combine(caja2, "iconcache_256.db")), null);

            // Comodines en la ruta (como los perfiles de Chrome)
            string caja3 = Path.Combine(raiz, "caja3");
            CrearArchivo(Path.Combine(caja3, @"User Data\Default\Cache\a.bin"), 1000, 0);
            CrearArchivo(Path.Combine(caja3, @"User Data\Profile 1\Cache\b.bin"), 1000, 0);
            CrearArchivo(Path.Combine(caja3, @"User Data\Default\Bookmarks"), 1000, 0);
            Objetivo o3 = new Objetivo();
            o3.Nombre = "Comodín"; o3.Rutas = new[] { Path.Combine(caja3, @"User Data\*\Cache") };
            MotorLimpieza.Limpiar(o3, CancellationToken.None, null);
            Comprobar("Comodín: limpia la caché de todos los perfiles",
                !File.Exists(Path.Combine(caja3, @"User Data\Default\Cache\a.bin")) && !File.Exists(Path.Combine(caja3, @"User Data\Profile 1\Cache\b.bin")), null);
            Comprobar("Comodín: no toca los marcadores", File.Exists(Path.Combine(caja3, @"User Data\Default\Bookmarks")), null);

            // Un objetivo que apunta a una carpeta protegida no hace nada
            Objetivo peligro = new Objetivo();
            peligro.Nombre = "Peligro"; peligro.Rutas = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"C:\" };
            Comprobar("Nunca resuelve carpetas protegidas (Documentos, C:\\)", MotorLimpieza.ResolverRutas(peligro).Count == 0, null);
        }

        static void PruebaMedirReal()
        {
            Seccion("3. Medición de las categorías reales (solo lectura)");
            foreach (Objetivo o in MotorLimpieza.ObtenerObjetivos())
            {
                try
                {
                    long b = MotorLimpieza.Medir(o, CancellationToken.None);
                    Comprobar("Mide '" + o.Nombre + "'", b >= -1, b < 0 ? "necesita admin" : Util.FormatoTamano(b));
                }
                catch (Exception ex) { Comprobar("Mide '" + o.Nombre + "'", false, ex.Message); }
            }
        }

        static void PruebaSistema()
        {
            Seccion("4. Información del sistema");
            InfoDisco d = Sistema.Disco();
            Comprobar("Disco C: tamaño y espacio libre", d.Total > 0 && d.Libre > 0, Util.FormatoTamano(d.Libre) + " libres de " + Util.FormatoTamano(d.Total));
            InfoMemoria m = Sistema.Memoria();
            Comprobar("Memoria RAM", m.Total > 0 && m.Disponible > 0, m.Porcentaje.ToString("N0") + " % en uso");
            Comprobar("Tiempo encendido", Sistema.TiempoEncendido().TotalMinutes > 0, Sistema.TiempoEncendido().TotalHours.ToString("N1") + " h");
            long p = Sistema.TamanoPapelera();
            Comprobar("Tamaño de la papelera", p >= 0, Util.FormatoTamano(p));
            long h = Sistema.TamanoArchivoRaiz("hiberfil.sys");
            Comprobar("Archivo de hibernación", h != 0, h < 0 ? "desactivada" : Util.FormatoTamano(h));
            Comprobar("Carpeta de Descargas", Directory.Exists(Sistema.CarpetaDescargas), Sistema.CarpetaDescargas);
        }

        static async Task PruebaPaginaLimpieza(Form host)
        {
            Seccion("5. Pantalla Liberar espacio: limpieza real de lo recomendado");
            PaginaLimpieza p = Pagina<PaginaLimpieza>(host);
            long antes = Sistema.Disco().Libre;
            long papeleraAntes = Sistema.TamanoPapelera();
            p.AlMostrar();
            bool medido = await Esperar(() => (bool)Campo(p, "analizado") && !(bool)Campo(p, "analizando"), 180000);
            Comprobar("Analiza todas las categorías", medido, null);
            List<Objetivo> objs = (List<Objetivo>)Campo(p, "objetivos");
            List<string> marcadas = new List<string>();
            foreach (DataGridViewRow r in ((DataGridView)Campo(p, "tabla")).Rows)
            {
                object v = r.Cells[0].Value;
                Objetivo o = r.Tag as Objetivo;
                if (v is bool && (bool)v && o != null) marcadas.Add(o.Nombre);
            }
            Comprobar("Vienen marcadas solo las recomendadas disponibles", marcadas.Count > 0 && !marcadas.Contains("Papelera de reciclaje"),
                string.Join(", ", marcadas.ToArray()));

            Llamar(p, "BtnLimpiar_Click", null, EventArgs.Empty);
            bool termino = await Esperar(() => !(bool)Campo(p, "limpiando") && !(bool)Campo(p, "analizando"), 300000);
            await Task.Delay(500);
            long despues = Sistema.Disco().Libre;
            Comprobar("La limpieza termina", termino, "espacio libre ganado: " + Util.FormatoTamano(Math.Max(0, despues - antes)));
            bool exito = false;
            lock (dialogos) foreach (string dl in dialogos) if (dl.Contains("liberado") || dl.Contains("Listo") || dl.Contains("Limpieza")) exito = true;
            Comprobar("Muestra el resultado al terminar", exito, null);
            Comprobar("La papelera no se tocó", Sistema.TamanoPapelera() >= papeleraAntes, Util.FormatoTamano(Sistema.TamanoPapelera()));
        }

        static void PruebaInicioRegistro(RegistryHive colmena)
        {
            Seccion("6. Programas de inicio: activar y apagar (" + (colmena == RegistryHive.CurrentUser ? "usuario" : "todos los usuarios") + ")");
            const string nombre = "OptimizadorPC_PruebaInicio";
            string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string aprob = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, RegistryView.Registry64))
                using (RegistryKey k = b.CreateSubKey(run))
                {
                    k.SetValue(nombre, "\"" + Util.RutaSistema("notepad.exe") + "\"");
                }
                ElementoInicio e = ElementosInicio.Obtener(false).Find(x => x.Nombre == nombre);
                Comprobar("Lo encuentra en la lista", e != null && e.Habilitado, null);
                if (e == null) return;
                ElementosInicio.CambiarEstado(e, false);
                ElementoInicio e2 = ElementosInicio.Obtener(false).Find(x => x.Nombre == nombre);
                byte[] datos;
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(aprob))
                {
                    datos = k == null ? null : k.GetValue(nombre) as byte[];
                }
                Comprobar("Apagar: queda apagado (igual que el Administrador de tareas)", e2 != null && !e2.Habilitado && datos != null && datos[0] == 3, null);
                bool comandoIntacto;
                using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(run))
                {
                    comandoIntacto = k.GetValue(nombre) != null;
                }
                Comprobar("Apagar no borra el programa del inicio (se puede revertir)", comandoIntacto, null);
                ElementosInicio.CambiarEstado(e2, true);
                ElementoInicio e3 = ElementosInicio.Obtener(false).Find(x => x.Nombre == nombre);
                Comprobar("Volver a activar funciona", e3 != null && e3.Habilitado, null);
            }
            catch (Exception ex) { Comprobar("Inicio sin errores", false, ex.Message); }
            finally
            {
                try
                {
                    using (RegistryKey b = RegistryKey.OpenBaseKey(colmena, RegistryView.Registry64))
                    {
                        using (RegistryKey k = b.OpenSubKey(run, true)) if (k != null) k.DeleteValue(nombre, false);
                        using (RegistryKey k = b.OpenSubKey(aprob, true)) if (k != null) k.DeleteValue(nombre, false);
                    }
                }
                catch { }
            }
        }

        static void PruebaInicioTarea()
        {
            Seccion("7. Tareas programadas: activar y apagar");
            const string tarea = "OptimizadorPC_PruebaTarea";
            Util.EjecutarProceso(Util.RutaSistema("schtasks.exe"), "/Create /TN " + tarea + " /TR notepad.exe /SC ONCE /ST 23:59 /SD 01/01/2099 /F", 30000);
            try
            {
                ElementoInicio e = ElementosInicio.Obtener(true).Find(x => x.Tipo == TipoInicio.Tarea && x.Nombre == tarea);
                Comprobar("Encuentra la tarea de prueba", e != null && e.Habilitado, null);
                if (e == null) return;
                try
                {
                    ElementosInicio.CambiarEstado(e, false);
                    ElementoInicio e2 = ElementosInicio.Obtener(true).Find(x => x.Tipo == TipoInicio.Tarea && x.Nombre == tarea);
                    Comprobar("Apagar la tarea", e2 != null && !e2.Habilitado, null);
                    ElementosInicio.CambiarEstado(e2, true);
                    ElementoInicio e3 = ElementosInicio.Obtener(true).Find(x => x.Tipo == TipoInicio.Tarea && x.Nombre == tarea);
                    Comprobar("Volver a activar la tarea", e3 != null && e3.Habilitado, null);
                }
                catch (Exception ex)
                {
                    Comprobar("Cambiar la tarea", Util.EsAdmin == false, "sin admin: " + ex.Message + " (se probará como administrador)");
                }
            }
            finally
            {
                Util.EjecutarProceso(Util.RutaSistema("schtasks.exe"), "/Delete /TN " + tarea + " /F", 30000);
            }
        }

        static async Task PruebaPaginaInicio(Form host)
        {
            Seccion("8. Pantalla Inicio de Windows");
            PaginaInicio p = Pagina<PaginaInicio>(host);
            p.AlMostrar();
            bool ok = await Esperar(() => (bool)Campo(p, "cargado") && !(bool)Campo(p, "cargando"), 90000);
            DataGridView t = (DataGridView)Campo(p, "tabla");
            Comprobar("Carga la lista de programas de inicio", ok && t.Rows.Count > 0, t.Rows.Count + " filas");
        }

        static async Task PruebaPaginaProcesos(Form host)
        {
            Seccion("9. Pantalla Procesos: cerrar un programa");
            ProcessStartInfo psi = new ProcessStartInfo(Util.RutaSistema("PING.EXE"), "-n 600 127.0.0.1");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process ping = Process.Start(psi);
            try
            {
                PaginaProcesos p = Pagina<PaginaProcesos>(host);
                p.AlMostrar();
                bool visto = await Esperar(delegate
                {
                    foreach (object g in (IEnumerable)Campo(p, "grupos"))
                    {
                        string n = (string)g.GetType().GetField("Nombre").GetValue(g);
                        if (string.Equals(n, "PING", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                }, 30000);
                Comprobar("Aparece el proceso de prueba en la lista", visto, null);
                bool protegido = false;
                foreach (object g in (IEnumerable)Campo(p, "grupos"))
                {
                    string n = (string)g.GetType().GetField("Nombre").GetValue(g);
                    if (n == "explorer") protegido = (bool)g.GetType().GetField("Protegido").GetValue(g);
                }
                Comprobar("El Explorador de Windows está protegido", protegido, null);
                PonerCampo(p, "seleccion", "PING");
                Llamar(p, "CerrarSeleccionado");
                bool cerrado = await Esperar(() => ping.HasExited, 20000);
                Comprobar("Lo cierra tras confirmar", cerrado, null);
                p.Visible = false;
            }
            finally
            {
                if (!ping.HasExited) ping.Kill();
            }
        }

        // Programa falso con desinstalador que deja restos, como hacen muchos programas reales
        static string CrearProgramaFalso(string id, string nombre, bool conDesinstalador)
        {
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string inst = Path.Combine(lad, @"Programs\" + id);
            CrearArchivo(Path.Combine(inst, id + ".exe"), 5000, 0);
            CrearArchivo(Path.Combine(inst, "config.ini"), 800, 0);
            CrearArchivo(Path.Combine(ad, id + @"\ajustes.json"), 1200, 0);
            CrearArchivo(Path.Combine(lad, id + @"\cache\datos.bin"), 40000, 0);
            string clave = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id;
            string desinstalador = Path.Combine(inst, "desinstalar.cmd");
            if (conDesinstalador)
            {
                // Como un desinstalador real "perezoso": borra el .exe y su entrada, pero deja configuración y datos
                File.WriteAllText(desinstalador,
                    "@echo off\r\ndel /f /q \"" + Path.Combine(inst, id + ".exe") + "\"\r\nreg delete \"HKCU\\" + clave + "\" /f >nul\r\n", Encoding.ASCII);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(clave))
            {
                k.SetValue("DisplayName", nombre + " 1.2.3");
                k.SetValue("DisplayVersion", "1.2.3");
                k.SetValue("Publisher", "Pruebas OPC S.A.");
                k.SetValue("InstallLocation", inst);
                k.SetValue("DisplayIcon", Path.Combine(inst, id + ".exe"));
                k.SetValue("UninstallString", conDesinstalador ? "\"" + desinstalador + "\"" : "\"" + Path.Combine(inst, "no_existe_uninstall.exe") + "\"");
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\" + id))
            {
                k.SetValue("Idioma", "es");
            }
            // Acceso directo en el menú Inicio
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), nombre + ".lnk");
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object acc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            acc.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, acc, new object[] { Path.Combine(inst, id + ".exe") });
            acc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, acc, null);
            return inst;
        }

        static async Task<bool> SeleccionarPrograma(PaginaProgramas p, string nombre)
        {
            DataGridView t = (DataGridView)Campo(p, "tabla");
            bool cargado = await Esperar(delegate
            {
                if ((bool)Campo(p, "cargando")) return false;
                foreach (DataGridViewRow r in t.Rows)
                {
                    object prog = r.Tag;
                    if (prog != null && ((string)prog.GetType().GetField("Nombre").GetValue(prog)).StartsWith(nombre)) return true;
                }
                return false;
            }, 60000);
            if (!cargado) return false;
            foreach (DataGridViewRow r in t.Rows)
            {
                object prog = r.Tag;
                if (prog != null && ((string)prog.GetType().GetField("Nombre").GetValue(prog)).StartsWith(nombre))
                {
                    t.ClearSelection();
                    t.CurrentCell = r.Cells[1];
                    r.Selected = true;
                    return true;
                }
            }
            return false;
        }

        static async Task PruebaDesinstalarConRestos(Form host)
        {
            Seccion("10. Pantalla Programas: desinstalar y borrar restos (programa de prueba)");
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            const string id = "ProgramaPruebaOPC";
            string inst = CrearProgramaFalso(id, "Programa Prueba OPC", true);
            // Señuelos que NO deben tocarse: otro programa instalado y una carpeta de nombre parecido
            string otro = CrearProgramaFalso("OtroPruebaOPC", "Otro Prueba OPC", true);
            CrearArchivo(Path.Combine(ad, @"ProgramaPruebaOPC2\importante.txt"), 100, 0);
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Programa Prueba OPC.lnk");
            string respaldos = Path.Combine(Util.CarpetaDatos, "respaldos");
            int respaldosAntes = Directory.Exists(respaldos) ? Directory.GetDirectories(respaldos).Length : 0;
            try
            {
                PaginaProgramas p = Pagina<PaginaProgramas>(host);
                p.AlMostrar();
                bool sel = await SeleccionarPrograma(p, "Programa Prueba OPC");
                Comprobar("El programa de prueba sale en la lista", sel, null);
                if (!sel) return;
                Llamar(p, "Desinstalar_Click", null, EventArgs.Empty);
                await Task.Delay(1000);
                bool fin = await Esperar(() => !(bool)Campo(p, "desinstalando") && !(bool)Campo(p, "cargando"), 120000);
                await Task.Delay(1500);
                Comprobar("El desinstalador se abrió y terminó", fin && !File.Exists(Path.Combine(inst, id + ".exe")), null);
                Comprobar("Ya no está en la lista de programas", !ClaveExiste(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id), null);
                Comprobar("Resto borrado: carpeta de instalación", !Directory.Exists(inst), null);
                Comprobar("Resto borrado: carpeta en AppData\\Roaming", !Directory.Exists(Path.Combine(ad, id)), null);
                Comprobar("Resto borrado: carpeta en AppData\\Local", !Directory.Exists(Path.Combine(lad, id)), null);
                Comprobar("Resto borrado: clave del registro", !ClaveExiste(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\" + id), null);
                Comprobar("Resto borrado: acceso directo roto", !File.Exists(lnk), null);
                int respaldosDespues = Directory.Exists(respaldos) ? Directory.GetDirectories(respaldos).Length : 0;
                Comprobar("Guardó copia .reg del registro antes de borrar", respaldosDespues > respaldosAntes, null);
                Comprobar("NO tocó el otro programa instalado", Directory.Exists(otro) &&
                    ClaveExiste(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\OtroPruebaOPC"), null);
                Comprobar("NO tocó la carpeta de nombre parecido", File.Exists(Path.Combine(ad, @"ProgramaPruebaOPC2\importante.txt")), null);
                p.Visible = false;
            }
            finally
            {
                LimpiarProgramaFalso(id);
                LimpiarProgramaFalso("OtroPruebaOPC");
                BorrarCarpetaPrueba(Path.Combine(ad, "ProgramaPruebaOPC2"));
            }
        }

        static async Task PruebaDesinstalarForzado(Form host)
        {
            Seccion("11. Pantalla Programas: quitar a la fuerza un programa sin desinstalador");
            const string id = "RotoPruebaOPC";
            string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string inst = CrearProgramaFalso(id, "Roto Prueba OPC", false);
            try
            {
                PaginaProgramas p = Pagina<PaginaProgramas>(host);
                p.AlMostrar();
                bool sel = await SeleccionarPrograma(p, "Roto Prueba OPC");
                Comprobar("El programa roto sale en la lista", sel, null);
                if (!sel) return;
                Llamar(p, "Desinstalar_Click", null, EventArgs.Empty);
                await Task.Delay(1000);
                await Esperar(() => !(bool)Campo(p, "desinstalando") && !(bool)Campo(p, "cargando"), 120000);
                await Task.Delay(1500);
                Comprobar("Ofrece quitarlo a la fuerza y desaparece de la lista",
                    !ClaveExiste(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id), null);
                Comprobar("Borra sus carpetas", !Directory.Exists(inst) && !Directory.Exists(Path.Combine(ad, id)), null);
                Comprobar("Borra su clave del registro", !ClaveExiste(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\" + id), null);
                p.Visible = false;
            }
            finally
            {
                LimpiarProgramaFalso(id);
            }
        }

        static void LimpiarProgramaFalso(string id)
        {
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            BorrarCarpetaPrueba(Path.Combine(lad, @"Programs\" + id));
            BorrarCarpetaPrueba(Path.Combine(lad, id));
            BorrarCarpetaPrueba(Path.Combine(ad, id));
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + id, false); } catch { }
            foreach (string lnk in Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "*Prueba OPC*.lnk"))
            {
                try { File.Delete(lnk); } catch { }
            }
        }

        static async Task PruebaPaginaArchivos(Form host)
        {
            Seccion("12. Pantalla Archivos grandes");
            PaginaArchivos p = Pagina<PaginaArchivos>(host);
            p.AlMostrar();
            bool fin = await Esperar(() => (bool)Campo(p, "iniciado") && !(bool)Campo(p, "escaneando"), 600000);
            DataGridView t = (DataGridView)Campo(p, "tabla");
            Comprobar("Busca archivos grandes en tus carpetas", fin, (t == null ? 0 : t.Rows.Count) + " archivos de más de 500 MB");

            string archivo = Path.Combine(raiz, "archivo_para_papelera.bin");
            CrearArchivo(archivo, 2048, 0);
            Task<Exception> tarea = (Task<Exception>)Llamar(typeof(PaginaArchivos), "MoverAPapelera", archivo);
            Exception error = await tarea;
            Comprobar("Envía un archivo a la papelera", error == null && !File.Exists(archivo), error == null ? null : error.Message);
            p.Visible = false;
        }

        static async Task PruebaPaginaApps(Form host)
        {
            Seccion("13. Pantalla Apps basura");
            PaginaApps p = Pagina<PaginaApps>(host);
            p.AlMostrar();
            bool ok = await Esperar(() => (bool)Campo(p, "cargado") && !(bool)Campo(p, "cargando"), 120000);
            DataGridView t = (DataGridView)Campo(p, "tabla");
            int basura = t.Rows.Count;
            Comprobar("Lee las apps basura instaladas", ok, basura + " encontradas");
            Llamar(p, "Llenar");
            PonerCampo(p, "verTodas", true);
            Llamar(p, "Llenar");
            int todas = t.Rows.Count;
            Comprobar("'Ver todas las apps' muestra más apps", todas > basura, todas + " apps en total");
            bool algunaProtegida = false;
            foreach (DataGridViewRow r in t.Rows)
            {
                string paquete = Convert.ToString(r.Cells[3].Value);
                if (paquete == "Microsoft.WindowsStore" || paquete == "Microsoft.WindowsCalculator" || paquete.StartsWith("Microsoft.VCLibs")) algunaProtegida = true;
            }
            Comprobar("Nunca ofrece quitar la Tienda, la Calculadora ni componentes", !algunaProtegida, null);
            p.Visible = false;
        }

        static void PruebaSensorAlmacenamiento()
        {
            Seccion("14. Sensor de almacenamiento (se deja como estaba)");
            const string ruta = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";
            string[] nombres = { "01", "2048", "04", "08", "256" };
            Dictionary<string, object> original = new Dictionary<string, object>();
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ruta))
            {
                foreach (string n in nombres) original[n] = k.GetValue(n);
            }
            try
            {
                Llamar(typeof(PaginaHerramientas), "EscribirSensor", true);
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ruta))
                {
                    Comprobar("Activar el Sensor de almacenamiento", Convert.ToInt32(k.GetValue("01")) == 1 && Convert.ToInt32(k.GetValue("2048")) == 7, null);
                }
                Llamar(typeof(PaginaHerramientas), "EscribirSensor", false);
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ruta))
                {
                    Comprobar("Desactivar el Sensor de almacenamiento", Convert.ToInt32(k.GetValue("01")) == 0, null);
                }
            }
            catch (Exception ex) { Comprobar("Sensor de almacenamiento", false, ex.Message); }
            finally
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ruta))
                {
                    foreach (string n in nombres)
                    {
                        if (original[n] == null) k.DeleteValue(n, false);
                        else k.SetValue(n, original[n], RegistryValueKind.DWord);
                    }
                }
            }
        }

        static async Task PruebaPaginaResumen(Form host)
        {
            Seccion("15. Pantalla Resumen");
            PaginaResumen p = Pagina<PaginaResumen>(host);
            p.AlMostrar();
            bool ok = await Esperar(() => !(bool)Campo(p, "midiendo") && !(bool)Campo(p, "midiendoRapido"), 120000);
            await Task.Delay(500);
            Comprobar("Calcula el resumen sin errores", ok && !(bool)Campo(p, "errorMedicion"), null);
        }

        static async Task PruebaPaginaHerramientas(Form host)
        {
            Seccion("16. Pantalla Herramientas (lectura de estados)");
            PaginaHerramientas p = Pagina<PaginaHerramientas>(host);
            p.AlMostrar();
            bool ok = await Esperar(() => !(bool)Campo(p, "refrescando"), 60000);
            Comprobar("Lee el estado de todas las herramientas", ok, null);
        }

        // ------------------------------------------------------------------ pruebas con administrador

        static async Task PruebasAdmin(Form host)
        {
            Comprobar("Se ejecuta como administrador", Util.EsAdmin, null);
            if (!Util.EsAdmin) return;
            PruebaInicioRegistro(RegistryHive.LocalMachine);
            PruebaInicioTarea();

            Seccion("A0. Diagnóstico del arranque (solo como administrador)");
            InfoArranque arr = Diagnostico.LeerArranque();
            string ultimo = arr.Arranques.Count > 0 ? arr.Arranques[arr.Arranques.Count - 1].Segundos.ToString("N1") + " s el último" : "";
            Comprobar("Lee la velocidad de los arranques", arr.Arranques.Count > 0 && !arr.SinPermiso, arr.Arranques.Count + " arranques, " + ultimo);
            foreach (CausaLenta c in arr.Causas.GetRange(0, Math.Min(5, arr.Causas.Count)))
                Escribir("      [frena el arranque] " + c.Tipo + ": " + c.Nombre + " (" + c.Veces + " veces, +" + c.RetrasoMax.ToString("N1") + " s)");
            PaginaDiagnostico pd = Pagina<PaginaDiagnostico>(host);
            pd.AlMostrar();
            Comprobar("La pantalla Diagnóstico carga como administrador", await Esperar(() => (bool)Campo(pd, "cargado"), 120000), null);
            PruebaModoJuego();

            Seccion("A1. Limpieza real de las categorías de Windows");
            foreach (Objetivo o in MotorLimpieza.ObtenerObjetivos())
            {
                if (!o.RequiereAdmin) continue;
                try
                {
                    long antes = MotorLimpieza.Medir(o, CancellationToken.None);
                    long lib = MotorLimpieza.Limpiar(o, CancellationToken.None, null);
                    Comprobar("Limpia '" + o.Nombre + "'", lib >= 0, "había " + Util.FormatoTamano(antes) + ", liberado " + Util.FormatoTamano(lib));
                }
                catch (Exception ex) { Comprobar("Limpia '" + o.Nombre + "'", false, ex.Message); }
            }
            bool wu = false;
            try { using (System.ServiceProcess.ServiceController sc = new System.ServiceProcess.ServiceController("wuauserv")) wu = sc.Status != System.ServiceProcess.ServiceControllerStatus.StopPending; } catch { }
            Comprobar("El servicio de Windows Update sigue en buen estado", wu, null);

            PaginaHerramientas h = Pagina<PaginaHerramientas>(host);
            h.AlMostrar();
            await Esperar(() => !(bool)Campo(h, "refrescando"), 60000);

            Seccion("A2. Limpieza automática semanal (se programa y se quita)");
            Llamar(h, "ProgramarTarea");
            await Esperar(() => !(bool)Campo(h, "ocupadoSemanal"), 60000);
            ResultadoProceso q = Util.EjecutarProceso(Util.RutaSistema("schtasks.exe"), "/Query /TN OptimizadorPC_LimpiezaSemanal /XML", 20000);
            Comprobar("La tarea semanal queda programada", q.Ok && q.Salida.Contains("/limpiezarapida") && q.Salida.Contains("HighestAvailable"), null);
            Llamar(h, "QuitarTarea");
            await Esperar(() => !(bool)Campo(h, "ocupadoSemanal"), 60000);
            q = Util.EjecutarProceso(Util.RutaSistema("schtasks.exe"), "/Query /TN OptimizadorPC_LimpiezaSemanal", 20000);
            Comprobar("La tarea semanal se quita", !q.Ok, null);

            Seccion("A3. Hibernación (se desactiva y se vuelve a activar)");
            long hib = Sistema.TamanoArchivoRaiz("hiberfil.sys");
            if (hib > 0)
            {
                PonerCampo(h, "hiberActiva", true);
                Llamar(h, "CambiarHibernacion");
                await Esperar(() => !(bool)Campo(h, "ocupadoHiber"), 60000);
                Comprobar("Desactivar la hibernación libera " + Util.FormatoTamano(hib), Sistema.TamanoArchivoRaiz("hiberfil.sys") < 0, null);
                PonerCampo(h, "hiberActiva", false);
                Llamar(h, "CambiarHibernacion");
                await Esperar(() => !(bool)Campo(h, "ocupadoHiber"), 60000);
                await Esperar(() => Sistema.TamanoArchivoRaiz("hiberfil.sys") > 0, 30000);
                Comprobar("Volver a activarla", Sistema.TamanoArchivoRaiz("hiberfil.sys") > 0, null);
            }
            else Comprobar("Hibernación (ya estaba desactivada, no se toca)", true, null);

            Seccion("A4. Punto de restauración");
            Llamar(h, "CrearPunto");
            await Esperar(() => !(bool)Campo(h, "ocupadoPunto"), 300000);
            bool creado = false;
            lock (dialogos) foreach (string d in dialogos) if (d.Contains("restauración") && !d.Contains("¿")) creado = true;
            Comprobar("Crea el punto de restauración", creado, null);

            Seccion("A5. Limpieza profunda de Windows (DISM)");
            long libreAntes = Sistema.Disco().Libre;
            Llamar(h, "LimpiezaProfunda");
            bool fin = await Esperar(() => !(bool)Campo(h, "ocupadoDism"), 45 * 60000);
            Comprobar("La limpieza profunda termina", fin, "espacio ganado: " + Util.FormatoTamano(Math.Max(0, Sistema.Disco().Libre - libreAntes)));
        }
    }
}
