using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OptimizadorPC
{
    /// <summary>Resumen: disco, memoria, lo que se puede liberar, datos rápidos y recomendaciones.</summary>
    class PaginaResumen : PaginaBase
    {
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
        private const long MB = 1024L * 1024;
        private const long GB = 1024L * 1024 * 1024;
        private const string IcoLuna = "";
        private const string IcoMemoria = "";
        private const string IcoRelojArena = "";

        private readonly Boton btnActualizar, btnOptimizar;
        private readonly AnilloUso anilloDisco, anilloRam;
        private readonly Label lblDisco, lblRam, lblLiberable, lblLiberableDetalle, icoNota, lblNota;
        private readonly Tarjeta tarjetaLiberar;
        private readonly BarraUso barraOptimizar;
        private readonly TarjetaDato datoInicio, datoReinicio, datoHibernacion, datoPapelera;
        private readonly Panel lista;
        private readonly List<FilaRecomendacion> filas = new List<FilaRecomendacion>();
        private readonly System.Windows.Forms.Timer reloj;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly string textoIncluye;

        // Datos medidos (-1 = sin medir; en hibernación y papelera -2 = sin medir)
        private InfoDisco disco = new InfoDisco();
        private InfoMemoria memoria = new InfoMemoria();
        private TimeSpan encendido = TimeSpan.Zero;
        private long liberable = -1;
        private int inicioActivos = -1;
        private long hibernacion = -2, papelera = -2;
        private DateTime ultimaMedicion = DateTime.MinValue;
        private bool midiendo, midiendoRapido, optimizando, repetirMedicion, errorMedicion;
        private string firmaRecomendaciones;

        public PaginaResumen() : base("Resumen", "El estado de tu PC de un vistazo")
        {
            textoIncluye = DescribirIncluye();

            btnActualizar = new Boton("Actualizar", EstiloBoton.Fantasma, Tema.IcoActualizar);
            btnActualizar.Margin = new Padding(0);
            btnActualizar.Click += delegate { Actualizar(true); };
            Acciones.Controls.Add(btnActualizar);

            TableLayoutPanel grilla = new TableLayoutPanel();
            grilla.Dock = DockStyle.Fill;
            grilla.Margin = new Padding(0);
            grilla.Padding = new Padding(0);
            grilla.ColumnCount = 1;
            grilla.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grilla.RowCount = 5;
            grilla.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(216)));
            grilla.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(16)));
            grilla.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(92)));
            grilla.RowStyles.Add(new RowStyle(SizeType.Absolute, Tema.S(16)));
            grilla.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ---- Fila 1: disco, memoria y lo que se puede liberar ----
            TableLayoutPanel fila1 = Fila(3);

            Tarjeta tDisco = NuevaTarjeta();
            Label icoDisco = Tema.Icono(Tema.IcoDisco, 12f, Tema.TextoSuave);
            Label titDisco = Tema.Etiqueta("Disco " + Sistema.LetraSistema, 11f, Tema.Texto, true);
            anilloDisco = NuevoAnillo("usado");
            lblDisco = Tema.Etiqueta("Leyendo…", 9.5f, Tema.TextoSuave);
            tDisco.Controls.AddRange(new Control[] { icoDisco, titDisco, anilloDisco, lblDisco });
            tDisco.Layout += delegate { ColocarAnillo(tDisco, icoDisco, titDisco, anilloDisco, lblDisco); };

            Tarjeta tRam = NuevaTarjeta();
            Label icoRam = Tema.Icono(IcoMemoria, 12f, Tema.TextoSuave);
            Label titRam = Tema.Etiqueta("Memoria RAM", 11f, Tema.Texto, true);
            anilloRam = NuevoAnillo("en uso");
            lblRam = Tema.Etiqueta("Leyendo…", 9.5f, Tema.TextoSuave);
            tRam.Controls.AddRange(new Control[] { icoRam, titRam, anilloRam, lblRam });
            tRam.Layout += delegate { ColocarAnillo(tRam, icoRam, titRam, anilloRam, lblRam); };

            Tarjeta tLiberar = NuevaTarjeta();
            tarjetaLiberar = tLiberar;
            Label icoLiberar = Tema.Icono(Tema.IcoLimpieza, 12f, Tema.TextoSuave);
            Label titLiberar = Tema.Etiqueta("Puedes liberar", 11f, Tema.Texto, true);
            lblLiberable = Tema.Etiqueta("Calculando…", 15f, Tema.TextoSuave, true);
            lblLiberableDetalle = Tema.Etiqueta(textoIncluye, 9f, Tema.TextoSuave);
            icoNota = Tema.Icono(Tema.IcoEscudo, 9f, Tema.TextoTenue);
            lblNota = Tema.Etiqueta("No toca tus archivos personales", 8.5f, Tema.TextoTenue);
            barraOptimizar = new BarraUso();
            barraOptimizar.Height = Tema.S(6);
            barraOptimizar.Visible = false;
            btnOptimizar = new Boton("Optimizar ahora", EstiloBoton.Primario, Tema.IcoPlay);
            btnOptimizar.AutoAncho = false;
            btnOptimizar.Margin = new Padding(0);
            btnOptimizar.Click += BtnOptimizar_Click;
            tLiberar.Controls.AddRange(new Control[] { icoLiberar, titLiberar, lblLiberable, lblLiberableDetalle, icoNota, lblNota, barraOptimizar, btnOptimizar });
            tLiberar.Layout += delegate { ColocarLiberar(tLiberar, icoLiberar, titLiberar); };

            fila1.Controls.Add(tDisco, 0, 0);
            fila1.Controls.Add(tRam, 2, 0);
            fila1.Controls.Add(tLiberar, 4, 0);

            // ---- Fila 2: datos rápidos (se pueden pulsar) ----
            TableLayoutPanel fila2 = Fila(4);
            datoInicio = new TarjetaDato(Tema.IcoInicio, "Programas al iniciar");
            datoInicio.Click += delegate { IrA("inicio"); };
            datoReinicio = new TarjetaDato(IcoRelojArena, "Días sin reiniciar");
            datoReinicio.Click += delegate { IrA("herramientas"); };
            datoHibernacion = new TarjetaDato(IcoLuna, "Hibernación");
            datoHibernacion.Click += delegate { IrA("herramientas"); };
            datoPapelera = new TarjetaDato(Tema.IcoLimpieza, "Papelera");
            datoPapelera.Click += delegate { IrA("limpieza"); };
            fila2.Controls.Add(datoInicio, 0, 0);
            fila2.Controls.Add(datoReinicio, 2, 0);
            fila2.Controls.Add(datoHibernacion, 4, 0);
            fila2.Controls.Add(datoPapelera, 6, 0);

            // ---- Fila 3: recomendaciones ----
            Tarjeta tRec = NuevaTarjeta();
            tRec.Padding = new Padding(Tema.S(20), Tema.S(16), Tema.S(12), Tema.S(6));
            Panel cabRec = new Panel();
            cabRec.Dock = DockStyle.Top;
            cabRec.Height = Tema.S(32);
            Label titRec = Tema.Etiqueta("Recomendaciones", 11f, Tema.Texto, true);
            titRec.Location = new Point(0, 0);
            cabRec.Controls.Add(titRec);
            lista = new Panel();
            lista.Dock = DockStyle.Fill;
            lista.AutoScroll = true;
            lista.ClientSizeChanged += delegate { ColocarFilas(); };
            lista.HandleCreated += delegate { Tema.ScrollOscuro(lista); };
            tRec.Controls.Add(lista);
            tRec.Controls.Add(cabRec);
            lista.BringToFront();

            grilla.Controls.Add(fila1, 0, 0);
            grilla.Controls.Add(fila2, 0, 2);
            grilla.Controls.Add(tRec, 0, 4);
            Contenido.Controls.Add(grilla);

            // La RAM y el disco se refrescan solos mientras la página está a la vista
            reloj = new System.Windows.Forms.Timer();
            reloj.Interval = 3000;
            reloj.Tick += delegate { if (!IsDisposed && Visible) ActualizarInstantaneo(); };
            VisibleChanged += delegate { if (!Visible) reloj.Stop(); };

            ActualizarDatos();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { cts.Cancel(); } catch { }
                reloj.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ ciclo de vida

        public override void AlMostrar()
        {
            Actualizar(false);
            reloj.Start();
        }

        /// <summary>Refresca todo. Con forzar=false lo pesado solo se vuelve a medir si tiene más de 60 s.</summary>
        private void Actualizar(bool forzar)
        {
            ActualizarInstantaneo();
            ActualizarRapido();
            bool viejo = ultimaMedicion == DateTime.MinValue || (DateTime.Now - ultimaMedicion).TotalSeconds > 60;
            if (forzar || (viejo && !midiendo)) MedirPesado();
        }

        // Disco, RAM y tiempo encendido: son instantáneos
        private void ActualizarInstantaneo()
        {
            disco = Sistema.Disco();
            memoria = Sistema.Memoria();
            encendido = Sistema.TiempoEncendido();

            if (disco.Total > 0)
            {
                anilloDisco.Valor = disco.Porcentaje;
                anilloDisco.ColorValor = Tema.ColorSegunUso(disco.Porcentaje);
                anilloDisco.TextoCentro = Porcentaje(disco.Porcentaje);
                lblDisco.Text = Gb(disco.Libre, 1) + " libres de " + Gb(disco.Total, disco.Total >= 100 * GB ? 0 : 1);
            }
            else
            {
                anilloDisco.Valor = 0;
                anilloDisco.TextoCentro = "—";
                lblDisco.Text = "No se pudo leer el disco";
            }

            if (memoria.Total > 0)
            {
                anilloRam.Valor = memoria.Porcentaje;
                anilloRam.ColorValor = Tema.ColorSegunUso(memoria.Porcentaje);
                anilloRam.TextoCentro = Porcentaje(memoria.Porcentaje);
                lblRam.Text = Gb(memoria.Usada, 1) + " de " + Gb(memoria.Total, 1) + " en uso";
            }
            else
            {
                anilloRam.Valor = 0;
                anilloRam.TextoCentro = "—";
                lblRam.Text = "No se pudo leer la memoria";
            }

            ActualizarDatos();
            CalcularRecomendaciones();
        }

        // Hibernación y papelera: rápidos, pero mejor fuera del hilo de la interfaz
        private async void ActualizarRapido()
        {
            if (midiendoRapido) return;
            midiendoRapido = true;
            try
            {
                long[] r = await Task.Run(() => new long[] { Sistema.TamanoArchivoRaiz("hiberfil.sys"), Sistema.TamanoPapelera() });
                if (IsDisposed) return;
                hibernacion = r[0];
                papelera = r[1];
                ActualizarDatos();
                CalcularRecomendaciones();
            }
            catch (Exception ex)
            {
                Util.Log("Resumen: no se pudo leer hibernación/papelera: " + ex.Message);
            }
            finally
            {
                midiendoRapido = false;
            }
        }

        // Programas de inicio y archivos basura recomendados: lo que más tarda
        private async void MedirPesado()
        {
            if (midiendo) { repetirMedicion = true; return; }
            midiendo = true;
            repetirMedicion = false;
            errorMedicion = false;
            CancellationToken ct = cts.Token;
            if (liberable < 0) MostrarLiberable();
            CalcularRecomendaciones();
            try
            {
                int activos = await Task.Run(() => ContarInicio());
                if (IsDisposed) return;
                inicioActivos = activos;
                ActualizarDatos();
                CalcularRecomendaciones();

                long total = await Task.Run(() => MedirLiberable(ct));
                if (IsDisposed) return;
                liberable = total;
                ultimaMedicion = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                errorMedicion = true;
                Util.Log("Resumen: error al medir: " + ex);
            }
            finally
            {
                midiendo = false;
            }
            if (IsDisposed) return;
            MostrarLiberable();
            CalcularRecomendaciones();
            if (repetirMedicion) MedirPesado();
        }

        private static int ContarInicio()
        {
            int n = 0;
            foreach (ElementoInicio e in ElementosInicio.Obtener(false))
            {
                if (e.Habilitado) n++;
            }
            return n;
        }

        private static long MedirLiberable(CancellationToken ct)
        {
            long total = 0;
            foreach (Objetivo o in MotorLimpieza.ObtenerObjetivos())
            {
                if (!o.Recomendado || !o.Disponible) continue;
                ct.ThrowIfCancellationRequested();
                try
                {
                    long t = MotorLimpieza.Medir(o, ct);
                    if (t > 0) total += t;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Util.Log("Resumen: no se pudo medir '" + o.Nombre + "': " + ex.Message); }
            }
            return total;
        }

        // ------------------------------------------------------------------ pintar datos

        private void MostrarLiberable()
        {
            if (optimizando) return;
            if (liberable < 0)
            {
                lblLiberable.Font = Tema.FuenteSemibold(15f);
                lblLiberable.ForeColor = Tema.TextoSuave;
                lblLiberable.Text = errorMedicion ? "No disponible" : "Calculando…";
            }
            else if (liberable < MB)
            {
                lblLiberable.Font = Tema.FuenteSemibold(18f);
                lblLiberable.ForeColor = Tema.Exito;
                lblLiberable.Text = "Todo limpio";
            }
            else
            {
                lblLiberable.Font = Tema.FuenteSemibold(24f);
                lblLiberable.ForeColor = Tema.Acento;
                lblLiberable.Text = Util.FormatoTamano(liberable);
            }
            lblLiberableDetalle.ForeColor = Tema.TextoSuave;
            lblLiberableDetalle.Text = liberable >= 0 && liberable < MB ? "No hay archivos basura que borrar ahora mismo." : textoIncluye;
        }

        private void ActualizarDatos()
        {
            if (inicioActivos < 0) datoInicio.Establecer("…", Tema.TextoSuave);
            else datoInicio.Establecer(inicioActivos.ToString(Es), inicioActivos >= 8 ? Tema.Aviso : Tema.Texto);

            double dias = encendido.TotalDays;
            datoReinicio.Establecer(dias.ToString("N1", Es), dias >= 7 ? Tema.Aviso : Tema.Texto);

            if (hibernacion == -2) datoHibernacion.Establecer("…", Tema.TextoSuave);
            else if (hibernacion < 0) datoHibernacion.Establecer("Desactivada", Tema.TextoSuave);
            else datoHibernacion.Establecer(Util.FormatoTamano(hibernacion), Tema.Texto);

            if (papelera == -2) datoPapelera.Establecer("…", Tema.TextoSuave);
            else if (papelera < 0) datoPapelera.Establecer("—", Tema.TextoSuave);
            else if (papelera == 0) datoPapelera.Establecer("Vacía", Tema.TextoSuave);
            else datoPapelera.Establecer(Util.FormatoTamano(papelera), papelera >= GB ? Tema.Aviso : Tema.Texto);
        }

        private void CalcularRecomendaciones()
        {
            List<Recomendacion> l = new List<Recomendacion>();
            if (disco.Total > 0 && disco.Porcentaje >= 85)
            {
                l.Add(new Recomendacion(Tema.IcoAviso, Tema.Aviso, "El disco está casi lleno",
                    "Solo quedan " + Gb(disco.Libre, 1) + " libres en " + disco.Unidad + ". Busca archivos grandes que ya no uses.", "archivos"));
            }
            if (liberable >= 500 * MB)
            {
                l.Add(new Recomendacion(Tema.IcoLimpieza, Tema.Acento, "Puedes liberar " + Util.FormatoTamano(liberable),
                    "Hay archivos basura que se pueden borrar sin riesgo.", "limpieza"));
            }
            if (inicioActivos >= 8)
            {
                l.Add(new Recomendacion(Tema.IcoInicio, Tema.Acento, "Se abren " + inicioActivos + " programas al encender el PC",
                    "Apaga los que no necesites y Windows arrancará más rápido.", "inicio"));
            }
            if (memoria.Total > 0 && memoria.Porcentaje >= 80)
            {
                l.Add(new Recomendacion(IcoMemoria, Tema.Aviso, "La memoria RAM está casi llena",
                    "Mira qué programas gastan más memoria y cierra los que no uses.", "procesos"));
            }
            int dias = (int)encendido.TotalDays;
            if (dias >= 7)
            {
                l.Add(new Recomendacion(IcoRelojArena, Tema.Acento, "Hace " + dias + " días que no reinicias",
                    "Reiniciar de vez en cuando libera memoria y termina de instalar las actualizaciones.", null));
            }
            if (hibernacion >= 2 * GB)
            {
                l.Add(new Recomendacion(IcoLuna, Tema.Acento, "La hibernación ocupa " + Util.FormatoTamano(hibernacion),
                    "Si no la usas, puedes desactivarla en Herramientas y recuperar ese espacio.", "herramientas"));
            }
            if (papelera >= GB)
            {
                l.Add(new Recomendacion(Tema.IcoLimpieza, Tema.Acento, "La papelera ocupa " + Util.FormatoTamano(papelera),
                    "Revisa que no haya nada importante y vacíala desde Liberar espacio.", "limpieza"));
            }
            if (!Util.EsAdmin)
            {
                l.Add(new Recomendacion(Tema.IcoEscudo, Tema.Acento, "Abre la app como administrador para poder limpiar más",
                    "Sin esos permisos no se pueden borrar los archivos basura de Windows.", null));
            }
            if (l.Count == 0)
            {
                if (liberable < 0 && midiendo)
                {
                    l.Add(new Recomendacion(Tema.IcoBuscar, Tema.TextoSuave, "Revisando tu PC…",
                        "En unos segundos verás aquí lo que puedes mejorar.", null));
                }
                else
                {
                    l.Add(new Recomendacion(Tema.IcoCheck, Tema.Exito, "Tu PC está en buen estado",
                        "No hemos encontrado nada importante que mejorar. ¡Buen trabajo!", null));
                }
            }
            MostrarRecomendaciones(l);
        }

        private void MostrarRecomendaciones(List<Recomendacion> l)
        {
            List<string> partes = new List<string>();
            foreach (Recomendacion r in l) partes.Add(r.Titulo + "|" + r.Detalle + "|" + r.Destino);
            string firma = string.Join("\n", partes);
            if (firma == firmaRecomendaciones) return;
            firmaRecomendaciones = firma;

            lista.SuspendLayout();
            foreach (FilaRecomendacion f in filas)
            {
                lista.Controls.Remove(f);
                f.Dispose();
            }
            filas.Clear();
            for (int i = 0; i < l.Count; i++)
            {
                Recomendacion r = l[i];
                FilaRecomendacion f = new FilaRecomendacion(r.Icono, r.Color, r.Titulo, r.Detalle, i > 0);
                if (r.Destino != null)
                {
                    string destino = r.Destino;
                    Boton b = new Boton("Ver  ›", EstiloBoton.Fantasma);
                    b.Click += delegate { IrA(destino); };
                    f.PonerBoton(b);
                }
                filas.Add(f);
                lista.Controls.Add(f);
            }
            lista.AutoScrollPosition = Point.Empty;
            ColocarFilas();
            lista.ResumeLayout();
        }

        private void ColocarFilas()
        {
            int ancho = lista.ClientSize.Width;
            int y = lista.AutoScrollPosition.Y;
            foreach (FilaRecomendacion f in filas)
            {
                f.SetBounds(0, y, ancho, f.Height);
                y += f.Height;
            }
        }

        // ------------------------------------------------------------------ optimizar

        private async void BtnOptimizar_Click(object sender, EventArgs e)
        {
            if (optimizando) return;
            Form dueno = FindForm();
            try
            {
                List<string> nombres = new List<string>();
                foreach (Objetivo o in MotorLimpieza.ObtenerObjetivos())
                {
                    if (o.Recomendado && o.Disponible) nombres.Add(o.Nombre);
                }
                if (nombres.Count == 0)
                {
                    Dialogo.Info(dueno, "No hay nada que limpiar", "Abre la app como administrador para poder limpiar los archivos de Windows.");
                    return;
                }

                string msg = "Se borrarán estos archivos basura:\n\n•  " + string.Join("\n•  ", nombres) +
                             "\n\nNo se toca ningún archivo personal ni la papelera.";
                if (!Util.EsAdmin)
                {
                    msg += "\n\nAlgunas cosas (como la caché de Windows Update) necesitan permisos de administrador y se saltarán.";
                }
                if (!Dialogo.Confirmar(dueno, "¿Optimizar ahora?", msg, "Optimizar", false)) return;

                optimizando = true;
                if (dueno != null) dueno.FormClosing += EvitarCierre;
                btnOptimizar.Enabled = false;
                btnOptimizar.Text = "Optimizando…";
                btnActualizar.Enabled = false;
                lblLiberableDetalle.ForeColor = Tema.Texto;
                lblLiberableDetalle.Text = "Preparando la limpieza…";
                barraOptimizar.Visible = true;
                barraOptimizar.Indeterminado = true;
                tarjetaLiberar.PerformLayout();

                long libreAntes = Sistema.Disco().Libre;
                Action<string> progreso = delegate(string m)
                {
                    EnUI(delegate { if (optimizando) lblLiberableDetalle.Text = m; });
                };
                long liberado = 0;
                Exception fallo = null;
                try
                {
                    liberado = await Task.Run(() => MotorLimpieza.LimpiezaRapida(CancellationToken.None, progreso));
                }
                catch (Exception ex)
                {
                    fallo = ex;
                    Util.Log("Resumen: error en la limpieza rápida: " + ex);
                }
                long libreDespues = Sistema.Disco().Libre;
                if (IsDisposed) return;

                optimizando = false;
                if (dueno != null) dueno.FormClosing -= EvitarCierre;
                barraOptimizar.Indeterminado = false;
                barraOptimizar.Visible = false;
                btnOptimizar.Text = "Optimizar ahora";
                btnOptimizar.Enabled = true;
                btnActualizar.Enabled = true;
                tarjetaLiberar.PerformLayout();

                if (fallo != null)
                {
                    Dialogo.Error(dueno, "No se pudo terminar la limpieza",
                        "Algo falló mientras se limpiaba. Lo que ya se había borrado, borrado está.\n\nEl detalle se guardó en el registro de la app.");
                }
                else
                {
                    string texto = liberado > 0
                        ? "Has liberado " + Util.FormatoTamano(liberado) + "."
                        : "No había casi nada que borrar: tu PC ya estaba limpio.";
                    long ganado = libreDespues - libreAntes;
                    if (ganado >= MB)
                    {
                        texto += "\nAhora tienes " + Util.FormatoTamano(ganado) + " más de espacio libre en " + Sistema.LetraSistema + ".";
                    }
                    if (!Util.EsAdmin)
                    {
                        texto += "\n\nAlgunas cosas necesitan permisos de administrador y se han saltado. Abre la app como administrador para limpiar más.";
                    }
                    Dialogo.Exito(dueno, liberado > 0 ? "¡Listo!" : "Todo limpio", texto);
                }

                liberable = -1;
                MostrarLiberable();
                Actualizar(true);
            }
            catch (Exception ex)
            {
                Util.Log("Resumen: error en Optimizar ahora: " + ex);
                optimizando = false;
                if (dueno != null) dueno.FormClosing -= EvitarCierre;
                if (!IsDisposed)
                {
                    barraOptimizar.Indeterminado = false;
                    barraOptimizar.Visible = false;
                    btnOptimizar.Text = "Optimizar ahora";
                    btnOptimizar.Enabled = true;
                    btnActualizar.Enabled = true;
                    tarjetaLiberar.PerformLayout();
                    MostrarLiberable();
                    Dialogo.Error(dueno, "Algo salió mal", "No se pudo completar la optimización.\n\nEl detalle se guardó en el registro de la app.");
                }
            }
        }

        private void EvitarCierre(object sender, FormClosingEventArgs e)
        {
            if (optimizando && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Dialogo.Info(FindForm(), "Espera un momento", "Se está limpiando tu PC. Podrás cerrar la app en cuanto termine.");
            }
        }

        private void EnUI(MethodInvoker accion)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(accion);
            }
            catch { }
        }

        // ------------------------------------------------------------------ colocación

        private static TableLayoutPanel Fila(int columnas)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Margin = new Padding(0);
            t.Padding = new Padding(0);
            t.RowCount = 1;
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            t.ColumnCount = columnas * 2 - 1;
            for (int i = 0; i < columnas; i++)
            {
                if (i > 0) t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Tema.S(16)));
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columnas));
            }
            return t;
        }

        private static Tarjeta NuevaTarjeta()
        {
            Tarjeta t = new Tarjeta();
            t.Dock = DockStyle.Fill;
            t.Margin = new Padding(0);
            return t;
        }

        private static AnilloUso NuevoAnillo(string subtexto)
        {
            AnilloUso a = new AnilloUso();
            a.Grosor = Tema.S(10);
            a.FuenteCentro = Tema.FuenteSemibold(19f);
            a.TextoCentro = "—";
            a.Subtexto = subtexto;
            return a;
        }

        private static void ColocarTitulo(Tarjeta t, Label ico, Label tit)
        {
            int p = t.Padding.Left;
            tit.Location = new Point(p + Tema.S(24), t.Padding.Top);
            ico.Location = new Point(p, tit.Top + (tit.Height - ico.Height) / 2 + Tema.S(1));
        }

        private static void ColocarAnillo(Tarjeta t, Label ico, Label tit, AnilloUso a, Label det)
        {
            int p = t.Padding.Left, w = t.ClientSize.Width, h = t.ClientSize.Height;
            ColocarTitulo(t, ico, tit);
            det.MaximumSize = new Size(Math.Max(Tema.S(40), w - 2 * p), 0);
            int arriba = tit.Bottom + Tema.S(8);
            int abajo = h - t.Padding.Bottom - det.Height - Tema.S(8);
            int lado = Math.Max(Tema.S(40), Math.Min(abajo - arriba, w - 2 * p));
            a.Size = new Size(lado, lado);
            a.Location = new Point((w - lado) / 2, arriba + Math.Max(0, (abajo - arriba - lado) / 2));
            det.Location = new Point((w - det.Width) / 2, h - t.Padding.Bottom - det.Height);
        }

        private void ColocarLiberar(Tarjeta t, Label ico, Label tit)
        {
            int p = t.Padding.Left, w = t.ClientSize.Width, h = t.ClientSize.Height;
            int ancho = Math.Max(Tema.S(40), w - 2 * p);
            ColocarTitulo(t, ico, tit);
            lblLiberable.Location = new Point(p - Tema.S(2), tit.Bottom + Tema.S(10));
            lblLiberableDetalle.MaximumSize = new Size(ancho, 0);
            lblLiberableDetalle.Location = new Point(p, lblLiberable.Bottom + Tema.S(2));
            btnOptimizar.Width = ancho;
            btnOptimizar.Location = new Point(p, h - t.Padding.Bottom - btnOptimizar.Height);
            barraOptimizar.Width = ancho;
            barraOptimizar.Location = new Point(p, btnOptimizar.Top - Tema.S(14) - barraOptimizar.Height);

            // Nota tranquilizadora encima del botón, solo si cabe sin pisar el texto
            int yNota = btnOptimizar.Top - Tema.S(12) - lblNota.Height;
            lblNota.Location = new Point(p + Tema.S(18), yNota);
            icoNota.Location = new Point(p, yNota + (lblNota.Height - icoNota.Height) / 2 + Tema.S(1));
            bool cabe = !optimizando && yNota >= lblLiberableDetalle.Bottom + Tema.S(8) && lblNota.Right <= w - p;
            lblNota.Visible = cabe;
            icoNota.Visible = cabe;
        }

        // ------------------------------------------------------------------ texto

        private static string Gb(long bytes, int decimales)
        {
            return (bytes / (double)GB).ToString("N" + decimales, Es) + " GB";
        }

        private static string Porcentaje(double valor)
        {
            return Math.Round(valor).ToString("0", Es) + "%";
        }

        /// <summary>Frase corta con lo que incluye "Optimizar ahora" (según los permisos que haya).</summary>
        private static string DescribirIncluye()
        {
            bool temp = false, wu = false, err = false, mini = false;
            List<string> otros = new List<string>();
            foreach (Objetivo o in MotorLimpieza.ObtenerObjetivos())
            {
                if (!o.Recomendado || !o.Disponible) continue;
                switch (o.Id)
                {
                    case "temp_usuario":
                    case "temp_windows": temp = true; break;
                    case "windows_update": wu = true; break;
                    case "errores": err = true; break;
                    case "miniaturas": mini = true; break;
                    default: otros.Add(Minuscula(o.Nombre)); break;
                }
            }
            List<string> partes = new List<string>();
            if (temp) partes.Add("temporales");
            if (wu) partes.Add("caché de Windows Update");
            if (err) partes.Add("informes de errores");
            if (mini) partes.Add("miniaturas");
            partes.AddRange(otros);
            if (partes.Count == 0) return "Abre la app como administrador para poder limpiar.";
            string s;
            if (partes.Count == 1) s = partes[0];
            else s = string.Join(", ", partes.GetRange(0, partes.Count - 1)) + " y " + partes[partes.Count - 1];
            return char.ToUpper(s[0], Es) + s.Substring(1) + ".";
        }

        private static string Minuscula(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return char.ToLower(s[0], Es) + s.Substring(1);
        }

        // ------------------------------------------------------------------ piezas propias

        private class Recomendacion
        {
            public readonly string Icono, Titulo, Detalle, Destino;
            public readonly Color Color;

            public Recomendacion(string icono, Color color, string titulo, string detalle, string destino)
            {
                Icono = icono;
                Color = color;
                Titulo = titulo;
                Detalle = detalle;
                Destino = destino;
            }
        }

        /// <summary>Tarjeta pequeña con un dato: etiqueta arriba, valor abajo e icono a la derecha. Se puede pulsar.</summary>
        private class TarjetaDato : Tarjeta
        {
            private readonly string icono, titulo;
            private string valor = "…";
            private Color colorValor = Tema.TextoSuave;
            private bool encima;

            public TarjetaDato(string icono, string titulo)
            {
                this.icono = icono;
                this.titulo = titulo;
                Dock = DockStyle.Fill;
                Margin = new Padding(0);
                Padding = new Padding(Tema.S(16));
                Cursor = Cursors.Hand;
            }

            public void Establecer(string texto, Color color)
            {
                if (texto == valor && color == colorValor) return;
                valor = texto;
                colorValor = color;
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                encima = true;
                BackColor = Tema.TarjetaHover;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                encima = false;
                BackColor = Tema.Tarjeta;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int p = Padding.Left;
                int lado = Tema.S(32);
                Rectangle ri = new Rectangle(Width - p - lado, Padding.Top, lado, lado);
                using (GraphicsPath gp = Tema.Redondeado(ri, Tema.S(8)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(encima ? 48 : 30, Tema.Acento)))
                {
                    g.FillPath(b, gp);
                }
                TextRenderer.DrawText(g, icono, Tema.FuenteIconos(12f), ri, Tema.Acento,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                Rectangle rt = new Rectangle(p, Padding.Top, Math.Max(Tema.S(20), ri.Left - Tema.S(8) - p), Tema.S(34));
                TextRenderer.DrawText(g, titulo, Tema.Fuente(9f), rt, Tema.TextoSuave,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                Font fv = Tema.FuenteSemibold(14f);
                int hv = TextRenderer.MeasureText(g, "Ag", fv, Size.Empty, TextFormatFlags.NoPadding).Height;
                Rectangle rv = new Rectangle(p, Height - Padding.Bottom - hv, Width - 2 * p, hv);
                TextRenderer.DrawText(g, valor, fv, rv, colorValor,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>Una fila de la lista de recomendaciones: icono, frase, detalle y botón opcional.</summary>
        private class FilaRecomendacion : Panel
        {
            private readonly string icono, titulo, detalle;
            private readonly Color color;
            private readonly bool separador;
            private Boton boton;

            public FilaRecomendacion(string icono, Color color, string titulo, string detalle, bool separador)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw, true);
                this.icono = icono;
                this.color = color;
                this.titulo = titulo;
                this.detalle = detalle;
                this.separador = separador;
                Height = Tema.S(58);
            }

            public void PonerBoton(Boton b)
            {
                boton = b;
                b.Margin = new Padding(0);
                Controls.Add(b);
                PerformLayout();
            }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                if (boton != null) boton.Location = new Point(Width - boton.Width - Tema.S(8), (Height - boton.Height) / 2);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                if (separador)
                {
                    using (Pen pen = new Pen(Tema.Borde)) g.DrawLine(pen, 0, 0, Width - Tema.S(8), 0);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int lado = Tema.S(34);
                Rectangle ri = new Rectangle(0, (Height - lado) / 2, lado, lado);
                using (GraphicsPath gp = Tema.Redondeado(ri, Tema.S(8)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(34, color)))
                {
                    g.FillPath(b, gp);
                }
                TextRenderer.DrawText(g, icono, Tema.FuenteIconos(12f), ri, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int x = ri.Right + Tema.S(14);
                int derecha = boton != null ? boton.Left - Tema.S(12) : Width - Tema.S(8);
                int ancho = Math.Max(Tema.S(20), derecha - x);
                Font ft = Tema.Fuente(10f), fd = Tema.Fuente(9f);
                TextFormatFlags f = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                int ht = TextRenderer.MeasureText(g, "Ag", ft, Size.Empty, TextFormatFlags.NoPadding).Height;
                int hd = TextRenderer.MeasureText(g, "Ag", fd, Size.Empty, TextFormatFlags.NoPadding).Height;
                int sep = Tema.S(3);
                int y = (Height - ht - sep - hd) / 2;
                TextRenderer.DrawText(g, titulo, ft, new Rectangle(x, y, ancho, ht), Tema.Texto, f);
                TextRenderer.DrawText(g, detalle, fd, new Rectangle(x, y + ht + sep, ancho, hd), Tema.TextoSuave, f);
            }
        }
    }
}
