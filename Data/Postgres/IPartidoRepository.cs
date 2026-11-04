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

        /// <summary>Partidos FINALIZADO con marcador de una liga y temporada (base de la tabla de posiciones).</summary>
        Task<IReadOnlyList<Partido>> ListarPartidosFinalizadosAsync(int idLiga, string temporada, CancellationToken ct = default);

        /// <summary>Un partido por id, en cualquier estado (el detalle tambien muestra EN_VIVO y FINALIZADO). null si no existe.</summary>
        Task<Partido?> ObtenerPartidoAsync(int idPartido, CancellationToken ct = default);

        /// <summary>
        /// TODAS las cuotas del partido, esten disponibles o no (con su campo Disponible): el detalle muestra los valores
        /// aunque esten deshabilitadas. A diferencia del home, aqui no se filtra por disponible.
        /// </summary>
        Task<IReadOnlyList<Cuota>> ListarCuotasDePartidoAsync(int idPartido, CancellationToken ct = default);

        /// <summary>
        /// Cuotas por id, disponibles o no (con su campo Disponible), en UNA consulta. Es la lectura con la que el servidor
        /// valida un cupon: nunca se confia en lo que enviaron el navegador o el cupon guardado.
        /// </summary>
        Task<IReadOnlyList<Cuota>> ListarCuotasPorIdsAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default);

        /// <summary>Partidos por id, en cualquier estado, en UNA consulta.</summary>
        Task<IReadOnlyList<Partido>> ListarPartidosPorIdsAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default);

        /// <summary>Los mercados (hoy 3: 1X2, total de goles 2.5 y ambos anotan), ordenados por id.</summary>
        Task<IReadOnlyList<Mercado>> ListarMercadosAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Calendario>> ListarCalendariosAsync(IReadOnlyCollection<int> idsCalendario, CancellationToken ct = default);
    }
}
