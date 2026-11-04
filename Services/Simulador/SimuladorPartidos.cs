using CasaApuestas.Data.Mongo;
using CasaApuestas.Data.Postgres;
using CasaApuestas.Models.Catalogo;
using CasaApuestas.Services.Presentacion;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services.Simulador
{
    /// <summary>
    /// Logica del simulador de partidos (RF13, etapa 1). El servicio en segundo plano (Background/SimuladorHostedService)
    /// llama a EjecutarCicloAsync cada pocos segundos; un ciclo hace, en orden:
    ///   1. Lee los partidos EN_VIVO de PostgreSQL (la BD es la verdad del minuto y del marcador) y retoma los que no conocia
    ///      (por ejemplo tras reiniciar la app).
    ///   2. Inicia UN partido PROGRAMADO cuya hora ya paso (arranque escalonado, con un maximo de partidos en vivo).
    ///   3. Avanza cada partido en vivo: minutos simulados -> goles y tarjetas al azar -> guarda minuto y marcador.
    ///   4. Al minuto 90 llama a registrar_resultado_partido (queda FINALIZADO).
    ///   5. Envia los eventos a MongoDB (si Mongo falla, se reintentan en el siguiente ciclo; el partido no se detiene).
    ///
    /// Todas las transiciones son idempotentes (UPDATE ... WHERE estado = ...): si otro proceso ya hizo el cambio, esta
    /// instancia lo ve (0 filas) y lo deja pasar. Nunca anula ni suspende partidos: eso solo lo hace el modo demostracion.
    /// Los dias/horas se comparan en hora de Colombia (FechaColombia), porque fecha_hora se guarda asi.
    /// </summary>
    public sealed class SimuladorPartidos
    {
        private const int MaxEventosPendientes = 2000;

        private readonly ISimuladorRepository _repo;
        private readonly IEventoPartidoRepository _eventos;
        private readonly IJugadoresCache _jugadores;
        private readonly SimuladorOptions _opc;
        private readonly TimeProvider _reloj;
        private readonly ILogger<SimuladorPartidos> _log;

        private readonly Dictionary<int, PartidoVivo> _vivos = new();
        private readonly List<EventoPartidoDocumento> _pendientes = new();
        private readonly object _candado = new();
        private long _ultimoInicio;
        private bool _huboInicio;

        /// <summary>Estado en memoria de un partido en vivo. Solo guarda lo que no esta en la BD (acumuladores y tarjetas).</summary>
        private sealed class PartidoVivo
        {
            public required int Id { get; init; }
            public required int IdLocal { get; init; }
            public required int IdVisitante { get; init; }
            public required double EsperadosLocal { get; init; }
            public required double EsperadosVisitante { get; init; }
            public required IReadOnlyList<Jugador> JugadoresLocal { get; init; }
            public required IReadOnlyList<Jugador> JugadoresVisitante { get; init; }
            public required Random Rng { get; init; }
            public int Minuto { get; set; }
            public int GolesLocal { get; set; }
            public int GolesVisitante { get; set; }
            public double FraccionMinuto { get; set; }
            public long UltimoTick { get; set; }
            public HashSet<int> ConAmarilla { get; } = new();
            public HashSet<int> Expulsados { get; } = new();
            public bool HuboRojaDirecta { get; set; }
        }

        public SimuladorPartidos(ISimuladorRepository repo, IEventoPartidoRepository eventos, IJugadoresCache jugadores,
            IOptions<SimuladorOptions> opciones, TimeProvider reloj, ILogger<SimuladorPartidos> log)
        {
            _repo = repo;
            _eventos = eventos;
            _jugadores = jugadores;
            _opc = opciones.Value;
            _reloj = reloj;
            _log = log;
        }

        private double SegundosPorMinuto => _opc.DuracionRealPartidoSegundos / (double)_opc.MinutosPartido;

        public async Task EjecutarCicloAsync(CancellationToken ct)
        {
            var ahora = FechaColombia.Ahora(_reloj);

            await SincronizarAsync(await _repo.ListarEnVivoAsync(ct), ct);
            await IniciarSiHayVencidoAsync(ahora, ct);

            foreach (var partido in _vivos.Values.ToList())
            {
                try
                {
                    await AvanzarAsync(partido, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un partido con problemas no detiene a los demas. Queda EN_VIVO con su ultimo minuto guardado y se
                    // reintenta en el siguiente ciclo.
                    _log.LogError(ex, "Error al avanzar el partido {IdPartido}; se reintentara en el siguiente ciclo.", partido.Id);
                }
            }

            await VaciarEventosAsync(ct);
        }

        // ---- 1. Sincronizar con la BD ------------------------------------------------------------------------------

        private async Task SincronizarAsync(IReadOnlyList<PartidoSim> enVivo, CancellationToken ct)
        {
            var idsBd = enVivo.Select(p => p.IdPartido).ToHashSet();
            foreach (var id in _vivos.Keys.Where(k => !idsBd.Contains(k)).ToList())
            {
                _vivos.Remove(id);
                _log.LogInformation("El partido {IdPartido} ya no esta EN_VIVO (lo finalizo o anulo otro proceso): se deja de simular.", id);
            }

            foreach (var fila in enVivo)
            {
                if (!_vivos.TryGetValue(fila.IdPartido, out var vivo))
                {
                    var minuto = fila.Minuto;
                    if (minuto is null)
                    {
                        // El seed deja los EN_VIVO sin minuto: se asume el configurado (35) y se guarda para no repetirlo.
                        minuto = _opc.MinutoSinDato;
                        await _repo.FijarMinutoSiNuloAsync(fila.IdPartido, minuto.Value, ct);
                        _log.LogWarning("El partido {IdPartido} estaba EN_VIVO sin minuto: se asume el minuto {Minuto} y se conserva el marcador {L}-{V}.",
                            fila.IdPartido, minuto, fila.GolesLocal ?? 0, fila.GolesVisitante ?? 0);
                    }
                    vivo = await CrearAsync(fila, minuto.Value, fila.GolesLocal ?? 0, fila.GolesVisitante ?? 0, ct);
                    _vivos[fila.IdPartido] = vivo;
                    _log.LogInformation("Se retoma el partido {IdPartido} desde el minuto {Minuto} ({L}-{V}).", fila.IdPartido, vivo.Minuto, vivo.GolesLocal, vivo.GolesVisitante);
                }
                else
                {
                    // La BD manda: si otro proceso cambio el marcador o el minuto, se adopta.
                    vivo.Minuto = fila.Minuto ?? vivo.Minuto;
                    vivo.GolesLocal = fila.GolesLocal ?? vivo.GolesLocal;
                    vivo.GolesVisitante = fila.GolesVisitante ?? vivo.GolesVisitante;
                }
            }
        }

        private async Task<PartidoVivo> CrearAsync(PartidoSim p, int minuto, int golesLocal, int golesVisitante, CancellationToken ct)
        {
            var cuotas = (await _repo.ObtenerCuotasIniciales1X2Async(new[] { p.IdPartido }, ct)).GetValueOrDefault(p.IdPartido);
            var (el, ev) = ModeloGoles.GolesEsperados(cuotas?.Local, cuotas?.Empate, cuotas?.Visitante, _opc.GolesEsperadosPorPartido, _opc.SesgoCuotas);

            return new PartidoVivo
            {
                Id = p.IdPartido,
                IdLocal = p.IdEquipoLocal,
                IdVisitante = p.IdEquipoVisitante,
                EsperadosLocal = el,
                EsperadosVisitante = ev,
                JugadoresLocal = await _jugadores.ObtenerAsync(p.IdEquipoLocal, ct),
                JugadoresVisitante = await _jugadores.ObtenerAsync(p.IdEquipoVisitante, ct),
                // Con semilla, cada partido tiene su propia secuencia reproducible.
                Rng = _opc.Semilla is int s ? new Random(unchecked(s * 31 + p.IdPartido)) : new Random(),
                Minuto = minuto,
                GolesLocal = golesLocal,
                GolesVisitante = golesVisitante,
                UltimoTick = _reloj.GetTimestamp()
            };
        }

        // ---- 2. Iniciar partidos --------------------------------------------------------------------------------

        private async Task IniciarSiHayVencidoAsync(DateTime ahora, CancellationToken ct)
        {
            if (_vivos.Count >= _opc.MaxPartidosEnVivo) return;
            if (_huboInicio && _reloj.GetElapsedTime(_ultimoInicio).TotalSeconds < _opc.EscalonInicioSegundos) return;

            var programados = await _repo.ListarProgramadosAsync(1, ct);
            if (programados.Count == 0) return;

            var candidato = programados[0];     // el de hora mas temprana
            if (candidato.FechaHora > ahora)
            {
                if (!_opc.AdelantarPartidosProgramados) return;
                // Modo demo: no hay ningun vencido y hay lugar para mas partidos -> se adelanta el siguiente a "ahora".
                if (!await _repo.AdelantarAsync(candidato.IdPartido, ahora, ct)) return;
                _log.LogInformation("Se adelanta el partido {IdPartido} (estaba para {Hora:yyyy-MM-dd HH:mm}) para mantener la actividad.", candidato.IdPartido, candidato.FechaHora);
            }

            // UPDATE ... WHERE estado = 'PROGRAMADO' AND fecha_hora <= ahora: si otro lo inicio, devuelve false.
            if (!await _repo.IniciarAsync(candidato.IdPartido, candidato.IdCalendario, ahora, ct)) return;

            _huboInicio = true;
            _ultimoInicio = _reloj.GetTimestamp();

            _vivos[candidato.IdPartido] = await CrearAsync(candidato, 0, 0, 0, ct);
            Encolar(Evento(candidato.IdPartido, "INICIO", 0, null, null, "Comienza el partido"));
            _log.LogInformation("Partido {IdPartido} INICIADO (EN_VIVO, minuto 0). En vivo ahora: {N}.", candidato.IdPartido, _vivos.Count);
        }

        // ---- 3 y 4. Avanzar y finalizar ----------------------------------------------------------------------------

        private async Task AvanzarAsync(PartidoVivo p, CancellationToken ct)
        {
            // Un partido que quedo en el minuto final (por un fallo al finalizar) se finaliza sin simular mas.
            if (p.Minuto >= _opc.MinutosPartido)
            {
                await FinalizarAsync(p, ct);
                return;
            }

            var ahora = _reloj.GetTimestamp();
            // Se limita el tiempo transcurrido: si el equipo estuvo dormido o en pausa, el partido no salta de golpe.
            var segundos = Math.Min(_reloj.GetElapsedTime(p.UltimoTick, ahora).TotalSeconds, 10.0 * _opc.IntervaloCicloSegundos);
            p.UltimoTick = ahora;

            p.FraccionMinuto += segundos / SegundosPorMinuto;
            var pasos = (int)Math.Floor(p.FraccionMinuto);
            if (pasos <= 0) return;
            p.FraccionMinuto -= pasos;

            var nuevos = new List<EventoPartidoDocumento>();
            for (var i = 0; i < pasos && p.Minuto < _opc.MinutosPartido; i++)
            {
                p.Minuto++;
                GenerarEventosDelMinuto(p, nuevos);
            }

            // Primero se guarda el progreso en PostgreSQL (la verdad); solo si se escribio se envian los eventos a Mongo.
            if (!await _repo.ActualizarProgresoAsync(p.Id, p.Minuto, p.GolesLocal, p.GolesVisitante, ct))
            {
                _vivos.Remove(p.Id);
                _log.LogInformation("El partido {IdPartido} ya no esta EN_VIVO: no se guarda el avance.", p.Id);
                return;
            }
            Encolar(nuevos);

            if (p.Minuto >= _opc.MinutosPartido) await FinalizarAsync(p, ct);
        }

        private void GenerarEventosDelMinuto(PartidoVivo p, List<EventoPartidoDocumento> eventos)
        {
            // Goles: cada equipo, con su probabilidad por minuto.
            if (ModeloGoles.OcurreEnUnMinuto(p.EsperadosLocal, _opc.MinutosPartido, p.Rng))
            {
                p.GolesLocal++;
                eventos.Add(EventoDeGol(p, p.IdLocal, p.JugadoresLocal, "local"));
                _log.LogInformation("GOL del local en el partido {IdPartido}, minuto {Minuto}: {L}-{V}", p.Id, p.Minuto, p.GolesLocal, p.GolesVisitante);
            }
            if (ModeloGoles.OcurreEnUnMinuto(p.EsperadosVisitante, _opc.MinutosPartido, p.Rng))
            {
                p.GolesVisitante++;
                eventos.Add(EventoDeGol(p, p.IdVisitante, p.JugadoresVisitante, "visitante"));
                _log.LogInformation("GOL del visitante en el partido {IdPartido}, minuto {Minuto}: {L}-{V}", p.Id, p.Minuto, p.GolesLocal, p.GolesVisitante);
            }

            // Tarjetas amarillas (la segunda del mismo jugador es roja) y rojas directas, que no afectan al marcador.
            if (ModeloGoles.OcurreEnUnMinuto(_opc.TarjetasAmarillasEsperadas, _opc.MinutosPartido, p.Rng))
            {
                var (idEquipo, plantilla) = p.Rng.Next(2) == 0 ? (p.IdLocal, p.JugadoresLocal) : (p.IdVisitante, p.JugadoresVisitante);
                var jugador = ModeloGoles.Elegir(plantilla, j => ModeloGoles.PesoTarjeta(j.Posicion), p.Expulsados, p.Rng);
                if (jugador is not null)
                {
                    if (p.ConAmarilla.Add(jugador.IdJugador))
                        eventos.Add(Evento(p.Id, "TARJETA_AMARILLA", p.Minuto, idEquipo, jugador.IdJugador, $"Amarilla para {Nombre(jugador)}"));
                    else
                    {
                        p.Expulsados.Add(jugador.IdJugador);
                        eventos.Add(Evento(p.Id, "TARJETA_ROJA", p.Minuto, idEquipo, jugador.IdJugador, $"Segunda amarilla y expulsion de {Nombre(jugador)}"));
                    }
                }
            }
            if (!p.HuboRojaDirecta && ModeloGoles.OcurreEnUnMinuto(_opc.ProbTarjetaRojaPorPartido, _opc.MinutosPartido, p.Rng))
            {
                var (idEquipo, plantilla) = p.Rng.Next(2) == 0 ? (p.IdLocal, p.JugadoresLocal) : (p.IdVisitante, p.JugadoresVisitante);
                var jugador = ModeloGoles.Elegir(plantilla, j => ModeloGoles.PesoTarjeta(j.Posicion), p.Expulsados, p.Rng);
                if (jugador is not null)
                {
                    p.HuboRojaDirecta = true;
                    p.Expulsados.Add(jugador.IdJugador);
                    eventos.Add(Evento(p.Id, "TARJETA_ROJA", p.Minuto, idEquipo, jugador.IdJugador, $"Roja directa para {Nombre(jugador)}"));
                }
            }
        }

        private EventoPartidoDocumento EventoDeGol(PartidoVivo p, int idEquipo, IReadOnlyList<Jugador> plantilla, string lado)
        {
            // El goleador sale de la plantilla REAL del catalogo (MySQL), con mas probabilidad para delanteros y mediocampistas.
            var autor = ModeloGoles.Elegir(plantilla, j => ModeloGoles.PesoGol(j.Posicion), p.Expulsados, p.Rng);
            return Evento(p.Id, "GOL", p.Minuto, idEquipo, autor?.IdJugador,
                autor is null ? $"Gol del {lado}" : $"Gol de {Nombre(autor)}");
        }

        private async Task FinalizarAsync(PartidoVivo p, CancellationToken ct)
        {
            // registrar_resultado_partido deja el partido FINALIZADO (minuto 90), cierra sus cuotas y devuelve el resultado de
            // cada cuota. Si otro proceso ya lo finalizo, devuelve null y aqui no hay nada mas que hacer.
            var resultados = await _repo.FinalizarAsync(p.Id, p.GolesLocal, p.GolesVisitante, ct);
            _vivos.Remove(p.Id);

            if (resultados is null)
            {
                _log.LogInformation("El partido {IdPartido} ya estaba finalizado.", p.Id);
                return;
            }

            Encolar(Evento(p.Id, "FIN", _opc.MinutosPartido, null, null, $"Final del partido: {p.GolesLocal}-{p.GolesVisitante}"));
            _log.LogInformation("Partido {IdPartido} FINALIZADO {L}-{V}; {N} cuotas con resultado ({G} ganadoras).",
                p.Id, p.GolesLocal, p.GolesVisitante, resultados.Count, resultados.Count(r => r.Ganadora));
            // E2: aqui se enviara (id_cuota, ganadora) a sp_ResolverApuestasPartido para liquidar las apuestas.
        }

        // ---- 5. Eventos hacia MongoDB ----------------------------------------------------------------------------

        private EventoPartidoDocumento Evento(int idPartido, string tipo, int minuto, int? idEquipo, int? idJugador, string descripcion) => new()
        {
            IdPartido = idPartido,
            TipoEvento = tipo,
            Minuto = minuto,
            IdEquipo = idEquipo,
            IdJugador = idJugador,
            Descripcion = descripcion,
            Creado = _reloj.GetUtcNow().UtcDateTime
        };

        private static string Nombre(Jugador j) => $"{j.Nombres} {j.Apellidos}".Trim();

        private void Encolar(params EventoPartidoDocumento[] eventos) => Encolar((IEnumerable<EventoPartidoDocumento>)eventos);

        private void Encolar(IEnumerable<EventoPartidoDocumento> eventos)
        {
            lock (_candado) _pendientes.AddRange(eventos);
        }

        private async Task VaciarEventosAsync(CancellationToken ct)
        {
            List<EventoPartidoDocumento> lote;
            lock (_candado)
            {
                if (_pendientes.Count == 0) return;
                lote = _pendientes.ToList();
                _pendientes.Clear();
            }

            try
            {
                var nuevos = await _eventos.GuardarAsync(lote, ct);
                _log.LogDebug("Eventos enviados a MongoDB: {N} (nuevos: {Nuevos}).", lote.Count, nuevos);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Mongo caido o lento: los partidos siguen avanzando. Los eventos vuelven a la cola y se reintentan (como el
                // envio es idempotente, reintentar nunca duplica). Si la cola crece demasiado se descartan los mas viejos.
                lock (_candado)
                {
                    _pendientes.InsertRange(0, lote);
                    var exceso = _pendientes.Count - MaxEventosPendientes;
                    if (exceso > 0) _pendientes.RemoveRange(0, exceso);
                }
                _log.LogWarning(ex, "No se pudieron guardar {N} eventos en MongoDB; se reintentara en el siguiente ciclo.", lote.Count);
            }
        }
    }
}
