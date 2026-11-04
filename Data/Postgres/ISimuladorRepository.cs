namespace CasaApuestas.Data.Postgres
{
    /// <summary>Un partido tal como lo necesita el simulador. Minuto y goles son NULL en un PROGRAMADO (y en el EN_VIVO del seed).</summary>
    public record PartidoSim(int IdPartido, int IdCalendario, int IdEquipoLocal, int IdEquipoVisitante, DateTime FechaHora,
        int? Minuto, int? GolesLocal, int? GolesVisitante);

    /// <summary>Resultado de una cuota al finalizar un partido (lo que devuelve registrar_resultado_partido).</summary>
    public record ResultadoCuotaSim(int IdCuota, bool Ganadora);

    /// <summary>Cuotas 1X2 INICIALES de un partido (base para repartir los goles esperados).</summary>
    public record Cuotas1X2(decimal? Local, decimal? Empate, decimal? Visitante);

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

        Task<IReadOnlyDictionary<int, Cuotas1X2>> ObtenerCuotasIniciales1X2Async(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default);
    }
}
