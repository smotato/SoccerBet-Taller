namespace CasaApuestas.Data.Postgres
{
    /// <summary>Un partido tal como lo necesita el simulador. Minuto y goles son NULL en un PROGRAMADO (y en el EN_VIVO del seed).</summary>
    public record PartidoSim(int IdPartido, int IdCalendario, int IdEquipoLocal, int IdEquipoVisitante, DateTime FechaHora,
        int? Minuto, int? GolesLocal, int? GolesVisitante);

    /// <summary>Resultado de una cuota al finalizar un partido (lo que devuelve registrar_resultado_partido).</summary>
    public record ResultadoCuotaSim(int IdCuota, bool Ganadora);

    /// <summary>Cuotas 1X2 INICIALES de un partido (base para repartir los goles esperados).</summary>
    public record Cuotas1X2(decimal? Local, decimal? Empate, decimal? Visitante);

    /// <summary>Una cuota de un partido con su valor INICIAL (la base para recalcular las cuotas en vivo).</summary>
    public record CuotaPartidoSim(int IdCuota, string CodigoSeleccion, decimal CuotaInicial);

    /// <summary>Ultima jornada creada (liga, temporada y numero): la siguiente continua desde aqui.</summary>
    public record UltimaJornada(int IdLiga, string Temporada, int Numero);

    public record MercadoSim(int IdMercado, string Nombre);

    public record NuevaCuota(int IdMercado, string Seleccion, string CodigoSeleccion, decimal Cuota);

    public record NuevoPartido(int IdEstadio, int IdEquipoLocal, int IdEquipoVisitante, DateTime FechaHora, IReadOnlyList<NuevaCuota> Cuotas);

    /// <summary>Una jornada completa a crear: calendario + partidos + cuotas, todo en una transaccion.</summary>
    public record NuevaJornada(int IdLiga, string Temporada, int Numero, DateTime FechaInicio, DateTime FechaFin, IReadOnlyList<NuevoPartido> Partidos);

    /// <summary>
    /// Escrituras y lecturas del simulador sobre PostgreSQL, con SQL explicito (PartidosDbContext sigue siendo de solo lectura).
    /// Cada transicion es IDEMPOTENTE: un UPDATE con la condicion de estado en el WHERE; devuelve false si otro ya la hizo.
    /// </summary>
    public interface ISimuladorRepository
    {
        Task<IReadOnlyList<PartidoSim>> ListarEnVivoAsync(CancellationToken ct = default);

        /// <summary>PROGRAMADOS por fecha y hora (los primeros N): sirve para encontrar los vencidos y el siguiente.</summary>
        Task<IReadOnlyList<PartidoSim>> ListarProgramadosAsync(int maximo, CancellationToken ct = default);

        /// <summary>PROGRAMADO -> EN_VIVO (minuto 0, marcador 0-0) solo si sigue PROGRAMADO y su hora ya llego. La jornada pasa a EN_CURSO.</summary>
        Task<bool> IniciarAsync(int idPartido, int idCalendario, DateTime ahora, CancellationToken ct = default);

        /// <summary>Mueve la hora de un PROGRAMADO a "ahora" (solo si sigue PROGRAMADO y su hora es futura).</summary>
        Task<bool> AdelantarAsync(int idPartido, DateTime ahora, CancellationToken ct = default);

        /// <summary>Guarda un minuto a un EN_VIVO que lo tiene NULL (nunca pisa un minuto existente).</summary>
        Task<bool> FijarMinutoSiNuloAsync(int idPartido, int minuto, CancellationToken ct = default);

        /// <summary>Guarda minuto y marcador de un EN_VIVO (false si ya no esta EN_VIVO o si el minuto retrocederia).</summary>
        Task<bool> ActualizarProgresoAsync(int idPartido, int minuto, int golesLocal, int golesVisitante, CancellationToken ct = default);

        /// <summary>
        /// Llama a registrar_resultado_partido. Devuelve (id_cuota, ganadora) de todas las cuotas, o null si el partido ya
        /// estaba finalizado/anulado (la funcion lo rechaza) y entonces no hay nada que hacer.
        /// </summary>
        Task<IReadOnlyList<ResultadoCuotaSim>?> FinalizarAsync(int idPartido, int golesLocal, int golesVisitante, CancellationToken ct = default);

        // ---- E3: cuotas en vivo ----

        /// <summary>Las cuotas del partido con su cuota inicial (cuota_inicial, o la cuota actual si fuera NULL).</summary>
        Task<IReadOnlyList<CuotaPartidoSim>> ObtenerCuotasBaseAsync(int idPartido, CancellationToken ct = default);

        /// <summary>
        /// Guarda las cuotas recalculadas (valor y disponibilidad) en UNA sentencia, solo si el partido sigue EN_VIVO: un
        /// partido ya finalizado o anulado nunca vuelve a abrir sus cuotas. Devuelve cuantas cuotas se actualizaron.
        /// </summary>
        Task<int> ActualizarCuotasVivoAsync(int idPartido, IReadOnlyCollection<(int IdCuota, decimal Cuota, bool Disponible)> cuotas, CancellationToken ct = default);

        // ---- E2: liquidacion y nueva jornada ----

        /// <summary>Resultado (ganadora o no) de las cuotas indicadas que pertenecen a partidos FINALIZADO. Las de otros partidos no se devuelven.</summary>
        Task<IReadOnlyList<ResultadoCuotaSim>> ObtenerResultadosFinalizadosAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default);

        /// <summary>De las cuotas indicadas, las que pertenecen a partidos SUSPENDIDO (anulados).</summary>
        Task<IReadOnlyList<int>> ObtenerCuotasDePartidosSuspendidosAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default);

        Task<int> ContarProgramadosAsync(CancellationToken ct = default);

        Task<UltimaJornada?> ObtenerUltimaJornadaAsync(CancellationToken ct = default);

        Task<IReadOnlyList<MercadoSim>> ListarMercadosAsync(CancellationToken ct = default);

        /// <summary>
        /// Crea la jornada (calendario PROGRAMADA + partidos PROGRAMADO + cuotas con cuota_inicial) en UNA transaccion.
        /// Devuelve los ids de partido creados, o null si esa jornada ya existia (otra instancia se adelanto: el UNIQUE de
        /// calendario lo impide y no se crea nada).
        /// </summary>
        Task<IReadOnlyList<int>?> CrearJornadaAsync(NuevaJornada jornada, CancellationToken ct = default);

        Task<IReadOnlyDictionary<int, Cuotas1X2>> ObtenerCuotasIniciales1X2Async(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default);
    }
}
