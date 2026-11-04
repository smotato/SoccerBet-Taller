using System.Text.RegularExpressions;
using CasaApuestas.Data.Postgres;
using CasaApuestas.Data.SqlServer;
using CasaApuestas.Models.Apuestas;
using CasaApuestas.Models.Partidos;
using CasaApuestas.Services.Presentacion;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services
{
    public interface IApuestaService
    {
        Task<ConfirmarApuestaResultado> ConfirmarAsync(int idCliente, ConfirmarApuestaRequest? solicitud, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "confirmar apuesta" (sencilla = 1 seleccion, combinada = 2 a 10).
    ///
    /// REGLA DE ORO: el navegador solo envia ids de cuota, la cuota que vio, el importe y la clave. TODO se vuelve a leer y a
    /// validar aqui, en el servidor, contra PostgreSQL (cuotas, partidos, mercados). Lo que haya guardado el cupon en el
    /// navegador puede estar viejo o manipulado: el servidor decide.
    ///
    /// Orden de las comprobaciones:
    ///   1. Idempotencia: si la clave YA creo una apuesta de ESTE cliente, se devuelve esa apuesta y no se valida ni cobra nada.
    ///   2. Forma: 1..N selecciones, sin ids repetidos, importe entero entre minimo y maximo.
    ///   3. Cada cuota: existe, partido PROGRAMADO, mercado activo, cuota disponible, una por mercado en cada partido.
    ///   4. Cuota cambiada: si alguna difiere de la que vio el cliente, se devuelven las nuevas y NO se apuesta.
    ///   5. Saldo (comodidad: da un mensaje claro). La autoridad es sp_RegistrarApuesta, que vuelve a comprobarlo
    ///      dentro de su transaccion.
    ///   6. sp_RegistrarApuesta con las cuotas LEIDAS POR EL SERVIDOR (nunca las del navegador).
    /// </summary>
    public class ApuestaService : IApuestaService
    {
        // Un GUID con guiones mide 36; el procedimiento acepta hasta 64.
        private static readonly Regex ClaveValida = new("^[A-Za-z0-9-]{8,64}$", RegexOptions.Compiled);

        private readonly IApuestaRepository _apuestas;
        private readonly IPartidoRepository _partidos;
        private readonly IEquiposCache _catalogo;
        private readonly ISaldoService _saldo;
        private readonly ApuestasOptions _opc;
        private readonly ILogger<ApuestaService> _log;

        public ApuestaService(IApuestaRepository apuestas, IPartidoRepository partidos, IEquiposCache catalogo,
            ISaldoService saldo, IOptions<ApuestasOptions> opciones, ILogger<ApuestaService> log)
        {
            _apuestas = apuestas;
            _partidos = partidos;
            _catalogo = catalogo;
            _saldo = saldo;
            _opc = opciones.Value;
            _log = log;
        }

        public async Task<ConfirmarApuestaResultado> ConfirmarAsync(int idCliente, ConfirmarApuestaRequest? solicitud, CancellationToken ct = default)
        {
            if (solicitud is null) return Invalida("No recibimos tu cupón. Inténtalo de nuevo.");

            var clave = solicitud.Clave?.Trim();
            if (clave is null || !ClaveValida.IsMatch(clave))
                return Invalida("Falta la clave del cupón. Recarga la página e inténtalo de nuevo.");

            // ---- 1. Idempotencia (siempre filtrada por el id_cliente de la SESION, no por nada que envie el navegador) ----
            var previa = await _apuestas.BuscarPorClaveAsync(idCliente, clave, ct);
            if (previa is not null) return await ExitoDesdeGuardadaAsync(idCliente, previa, ct);

            // ---- 2. Forma del cupon ----
            var sels = solicitud.Selecciones ?? new List<SeleccionRequest>();
            var errores = new List<ErrorSeleccion>();

            if (sels.Count == 0) errores.Add(new(null, null, null, "El cupón está vacío."));
            else if (sels.Count > _opc.MaxSelecciones)
                errores.Add(new(null, null, null, $"Un cupón admite como máximo {_opc.MaxSelecciones} selecciones."));

            foreach (var repetida in sels.GroupBy(s => s.IdCuota).Where(g => g.Count() > 1))
                errores.Add(new(repetida.Key, null, null, $"La cuota {repetida.Key} está repetida en el cupón."));

            if (solicitud.Importe != decimal.Truncate(solicitud.Importe))
                errores.Add(new(null, null, null, "El importe debe ser en pesos enteros."));
            else if (solicitud.Importe < _opc.ImporteMinimo)
                errores.Add(new(null, null, null, $"El importe mínimo es {Pesos.Formatear(_opc.ImporteMinimo)}."));
            else if (solicitud.Importe > _opc.ImporteMaximo)
                errores.Add(new(null, null, null, $"El importe máximo es {Pesos.Formatear(_opc.ImporteMaximo)}."));

            if (errores.Count > 0) return Invalida("Revisa tu cupón.", errores);

            // ---- 3. Cada cuota se relee en PostgreSQL ----
            var ctx = await CargarContextoAsync(sels.Select(s => s.IdCuota).ToList(), ct);
            var vistos = new Dictionary<(int Partido, int Mercado), string>();
            var validas = new List<(SeleccionRequest Pedida, Cuota Actual)>();

            foreach (var s in sels)
            {
                if (!ctx.Cuotas.TryGetValue(s.IdCuota, out var c))
                {
                    errores.Add(new(s.IdCuota, null, null, $"La cuota {s.IdCuota} ya no existe."));
                    continue;
                }
                var t = ctx.Textos(c);
                var quien = $"{t.Seleccion} ({t.Mercado}, {t.Partido})";

                if (!ctx.Partidos.TryGetValue(c.IdPartido, out var partido))
                    errores.Add(new(c.IdCuota, c.IdPartido, c.IdMercado, $"{quien}: el partido ya no existe."));
                else if (partido.Estado != "PROGRAMADO")
                    errores.Add(new(c.IdCuota, c.IdPartido, c.IdMercado, $"{quien}: {EstadoLegible(partido.Estado)}, ya no admite apuestas."));
                else if (!ctx.Mercados.TryGetValue(c.IdMercado, out var mercado) || !mercado.Activo)
                    errores.Add(new(c.IdCuota, c.IdPartido, c.IdMercado, $"{quien}: el mercado está cerrado."));
                else if (!c.Disponible)
                    errores.Add(new(c.IdCuota, c.IdPartido, c.IdMercado, $"{quien}: la cuota ya no está disponible."));
                else if (vistos.TryGetValue((c.IdPartido, c.IdMercado), out var otra))
                    errores.Add(new(c.IdCuota, c.IdPartido, c.IdMercado,
                        $"{quien}: solo se permite una selección por mercado en cada partido y ya elegiste «{otra}»."));
                else
                {
                    vistos[(c.IdPartido, c.IdMercado)] = t.Seleccion;
                    validas.Add((s, c));
                }
            }

            var vigentes = sels.Where(s => ctx.Cuotas.ContainsKey(s.IdCuota))
                               .Select(s => new SeleccionRequest { IdCuota = s.IdCuota, Cuota = ctx.Cuotas[s.IdCuota].Valor }).ToList();

            if (errores.Count > 0)
            {
                _log.LogInformation("Cupon rechazado para el cliente {IdCliente}: {N} seleccion(es) invalida(s).", idCliente, errores.Count);
                return Invalida($"{errores.Count} selección(es) de tu cupón no se pueden apostar. Quítalas para continuar.", errores, vigentes);
            }

            // ---- 4. Cuotas que cambiaron desde que el cliente las vio ----
            var cambios = validas
                .Where(v => decimal.Round(v.Pedida.Cuota, 4) != decimal.Round(v.Actual.Valor, 4))
                .Select(v =>
                {
                    var t = ctx.Textos(v.Actual);
                    return new CambioCuota(v.Actual.IdCuota, t.Partido, t.Mercado, t.Seleccion, v.Pedida.Cuota, v.Actual.Valor);
                }).ToList();
            if (cambios.Count > 0)
            {
                return new ConfirmarApuestaResultado
                {
                    Exito = false, Codigo = "CUOTAS_CAMBIADAS", Status = 409,
                    Mensaje = "Algunas cuotas cambiaron. Revisa las nuevas cuotas y confirma de nuevo si quieres apostar.",
                    Cambios = cambios, CuotasActuales = vigentes
                };
            }

            // ---- 5. Saldo (aviso claro; la autoridad es el procedimiento) ----
            var saldo = await _saldo.ObtenerSaldoAsync(idCliente, ct);
            if (saldo < solicitud.Importe) return SaldoInsuficiente(saldo, solicitud.Importe, vigentes);

            // ---- 6. Registrar con las cuotas LEIDAS POR EL SERVIDOR ----
            ResultadoRegistro res;
            try
            {
                res = await _apuestas.RegistrarAsync(idCliente, validas.Select(v => (v.Actual.IdCuota, v.Actual.Valor)).ToList(),
                    solicitud.Importe, clave, _opc.PremioMaximo, ct);
            }
            catch (ApuestaRechazadaException ex)
            {
                _log.LogWarning("sp_RegistrarApuesta rechazo la apuesta del cliente {IdCliente} (error {Numero}).", idCliente, ex.Numero);
                if (ex.Numero == 50024)
                    return SaldoInsuficiente(await _saldo.ObtenerSaldoAsync(idCliente, ct), solicitud.Importe, vigentes);
                return new ConfirmarApuestaResultado { Exito = false, Codigo = "RECHAZADA", Status = 422, Mensaje = TraducirError(ex.Numero), CuotasActuales = vigentes };
            }

            // Carrera: dos peticiones simultaneas con la misma clave. La segunda recibe repetida = 1 del procedimiento.
            if (res.Repetida)
            {
                var guardada = await _apuestas.BuscarPorClaveAsync(idCliente, clave, ct);
                if (guardada is not null) return await ExitoDesdeGuardadaAsync(idCliente, guardada, ct);
            }

            var registrada = new ApuestaRegistrada(res.IdApuesta, res.CuotaTotal, solicitud.Importe, res.PosibleGanancia, res.Repetida,
                validas.Select(v => Registrada(ctx, v.Actual, v.Actual.Valor)).ToList());
            return await ExitoAsync(idCliente, registrada, ct);
        }

        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Traduccion de los errores controlados de sp_RegistrarApuesta (el 50024, saldo, se trata aparte).</summary>
        private string TraducirError(int numero) => numero switch
        {
            50020 => "El importe de la apuesta debe ser mayor que cero.",
            50021 => "Tu cuenta de cliente no existe o no está activa.",
            50023 => "Alguna de las cuotas recibidas no es válida.",
            50025 => $"El cupón debe tener entre 1 y {_opc.MaxSelecciones} selecciones.",
            50027 => "Falta la clave del cupón. Recarga la página e inténtalo de nuevo.",
            _ => "No pudimos registrar la apuesta. Inténtalo de nuevo."
        };

        private static ConfirmarApuestaResultado SaldoInsuficiente(decimal saldo, decimal importe, IReadOnlyList<SeleccionRequest> vigentes) => new()
        {
            Exito = false, Codigo = "SALDO", Status = 422,
            Mensaje = $"Saldo insuficiente. Tu saldo es {Pesos.Formatear(saldo)} y la apuesta es de {Pesos.Formatear(importe)}.",
            Saldo = saldo, SaldoTexto = Pesos.Formatear(saldo), CuotasActuales = vigentes
        };

        private static ConfirmarApuestaResultado Invalida(string mensaje, IReadOnlyList<ErrorSeleccion>? errores = null,
            IReadOnlyList<SeleccionRequest>? vigentes = null) => new()
        {
            Exito = false, Codigo = "INVALIDA", Status = 422, Mensaje = mensaje,
            Errores = errores ?? Array.Empty<ErrorSeleccion>(), CuotasActuales = vigentes ?? Array.Empty<SeleccionRequest>()
        };

        private static string EstadoLegible(string estado) => estado switch
        {
            "EN_VIVO" => "el partido ya empezó",
            "FINALIZADO" => "el partido ya terminó",
            "SUSPENDIDO" => "el partido fue suspendido",
            _ => "el partido no está disponible"
        };

        private async Task<ConfirmarApuestaResultado> ExitoDesdeGuardadaAsync(int idCliente, ApuestaGuardada g, CancellationToken ct)
        {
            // Los textos (partido, mercado, seleccion) se buscan en PostgreSQL/MySQL; si no estan disponibles se muestran
            // genericos. La cuota que se muestra es siempre la GUARDADA en la apuesta.
            Contexto ctx;
            try { ctx = await CargarContextoAsync(g.Selecciones.Select(s => s.IdCuota).ToList(), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudieron leer los textos de la apuesta {IdApuesta}.", g.IdApuesta);
                ctx = Contexto.Vacio;
            }
            var sel = g.Selecciones.Select(s => ctx.Cuotas.TryGetValue(s.IdCuota, out var c)
                ? Registrada(ctx, c, s.Cuota)
                : new SeleccionRegistrada(s.IdCuota, "Partido", "Mercado", $"Cuota {s.IdCuota}", s.Cuota)).ToList();
            return await ExitoAsync(idCliente, new ApuestaRegistrada(g.IdApuesta, g.CuotaTotal, g.Importe, g.PosibleGanancia, true, sel), ct);
        }

        private async Task<ConfirmarApuestaResultado> ExitoAsync(int idCliente, ApuestaRegistrada apuesta, CancellationToken ct)
        {
            decimal? saldo = null;
            string? texto = null;
            try
            {
                var s = await _saldo.ObtenerSaldoAsync(idCliente, ct);
                saldo = s;
                texto = Pesos.Formatear(s);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo leer el saldo tras registrar la apuesta {IdApuesta}.", apuesta.IdApuesta);
            }

            return new ConfirmarApuestaResultado
            {
                Exito = true, Status = 200,
                Codigo = apuesta.Repetida ? "REPETIDA" : "OK",
                Mensaje = apuesta.Repetida
                    ? "Esta apuesta ya estaba registrada (reenvío del mismo cupón). No se cobró de nuevo."
                    : "Tu apuesta quedó registrada y el importe se descontó de tu saldo.",
                Apuesta = apuesta, Saldo = saldo, SaldoTexto = texto
            };
        }

        private static SeleccionRegistrada Registrada(Contexto ctx, Cuota c, decimal cuota)
        {
            var t = ctx.Textos(c);
            return new SeleccionRegistrada(c.IdCuota, t.Partido, t.Mercado, t.Seleccion, cuota);
        }

        // ---- Carga de datos (PostgreSQL para validar; MySQL solo para los nombres) ----

        private sealed class Contexto
        {
            public static readonly Contexto Vacio = new(new(), new(), new(), new());

            public Dictionary<int, Cuota> Cuotas { get; }
            public Dictionary<int, Partido> Partidos { get; }
            public Dictionary<int, Mercado> Mercados { get; }
            public Dictionary<int, (string Local, string Visitante)> Equipos { get; }

            public Contexto(Dictionary<int, Cuota> cuotas, Dictionary<int, Partido> partidos, Dictionary<int, Mercado> mercados,
                Dictionary<int, (string, string)> equiposPorPartido)
            {
                Cuotas = cuotas; Partidos = partidos; Mercados = mercados; Equipos = equiposPorPartido;
            }

            public (string Partido, string Mercado, string Seleccion) Textos(Cuota c)
            {
                var hay = Equipos.TryGetValue(c.IdPartido, out var e);
                var local = hay ? e.Local : "Local";
                var visita = hay ? e.Visitante : "Visitante";
                var partido = hay ? $"{local} vs {visita}" : $"Partido #{c.IdPartido}";
                var mercado = Mercados.TryGetValue(c.IdMercado, out var m) ? m.Nombre : $"Mercado #{c.IdMercado}";
                return (partido, mercado, EtiquetaCuota.Texto(c, local, visita));
            }
        }

        private async Task<Contexto> CargarContextoAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct)
        {
            // PostgreSQL: lo que valida el servidor. Si esta caido la excepcion sube y el controlador responde 503.
            var cuotas = (await _partidos.ListarCuotasPorIdsAsync(idsCuota, ct)).ToDictionary(c => c.IdCuota);
            var partidos = (await _partidos.ListarPartidosPorIdsAsync(cuotas.Values.Select(c => c.IdPartido).Distinct().ToList(), ct))
                .ToDictionary(p => p.IdPartido);
            var mercados = (await _partidos.ListarMercadosAsync(ct)).ToDictionary(m => m.IdMercado);

            // MySQL: solo nombres para los mensajes. Si falla no se bloquea la apuesta: se usan textos genericos.
            var equiposPorPartido = new Dictionary<int, (string, string)>();
            try
            {
                var nombres = await _catalogo.ObtenerEquiposAsync(partidos.Values.SelectMany(p => new[] { p.IdEquipoLocal, p.IdEquipoVisitante }), ct);
                foreach (var p in partidos.Values)
                    if (nombres.TryGetValue(p.IdEquipoLocal, out var l) && nombres.TryGetValue(p.IdEquipoVisitante, out var v))
                        equiposPorPartido[p.IdPartido] = (l.Nombre, v.Nombre);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudieron leer los nombres de los equipos para los mensajes del cupon.");
            }
            return new Contexto(cuotas, partidos, mercados, equiposPorPartido);
        }
    }
}
