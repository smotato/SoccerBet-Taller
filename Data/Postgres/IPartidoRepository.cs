using CasaApuestas.Models.Partidos;

namespace CasaApuestas.Data.Postgres
{
    /// <summary>
    /// Lectura de partidos y cuotas. Los servicios solo conocen esta interfaz: la fuente (seed en PostgreSQL hoy,
    /// quiza una API mas adelante) puede cambiar sin tocar controladores. Devuelve los datos tal cual; combinarlos con
    /// los nombres de equipos (MySQL) y darles formato es trabajo del servicio.
    /// </summary>
    public interface IPartidoRepository
    {
        /// <summary>Partidos EN_VIVO y PROGRAMADO, ordenados por fecha y hora.</summary>
        Task<IReadOnlyList<Partido>> ListarPartidosActivosAsync(CancellationToken ct = default);

        /// <summary>Cuotas disponibles (de mercados activos) de los partidos indicados, en UNA sola consulta.</summary>
        Task<IReadOnlyList<Cuota>> ListarCuotasDisponiblesAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default);

        Task<IReadOnlyList<Calendario>> ListarCalendariosAsync(IReadOnlyCollection<int> idsCalendario, CancellationToken ct = default);
    }
}
