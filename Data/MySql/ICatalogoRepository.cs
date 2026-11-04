using CasaApuestas.Models.Catalogo;

namespace CasaApuestas.Data.MySql
{
    /// <summary>Un estadio asociado a un equipo (fila de equipo_estadio + datos del estadio).</summary>
    public record EstadioDeEquipo(int IdEquipo, bool EsPrincipal, Estadio Estadio);

    /// <summary>
    /// Lectura del catalogo deportivo. Los servicios solo conocen esta interfaz, no que el gestor sea MySQL.
    /// Solo devuelve los datos tal cual estan guardados; filtrar u ordenar para mostrar es trabajo del servicio.
    /// </summary>
    public interface ICatalogoRepository
    {
        Task<Liga?> ObtenerLigaActivaAsync(CancellationToken ct = default);
        Task<Liga?> ObtenerLigaAsync(int idLiga, CancellationToken ct = default);
        Task<IReadOnlyList<Equipo>> ListarEquiposAsync(CancellationToken ct = default);
        Task<Equipo?> ObtenerEquipoAsync(int idEquipo, CancellationToken ct = default);

        /// <summary>Varios equipos en UNA sola consulta (WHERE id_equipo IN (...)); los ids que no existan simplemente no vienen.</summary>
        Task<IReadOnlyList<Equipo>> ListarEquiposPorIdsAsync(IReadOnlyCollection<int> idsEquipo, CancellationToken ct = default);
        Task<IReadOnlyList<EstadioDeEquipo>> ListarEstadiosDeEquiposAsync(IReadOnlyCollection<int> idsEquipo, CancellationToken ct = default);
        Task<IReadOnlyList<Jugador>> ListarJugadoresDeEquipoAsync(int idEquipo, CancellationToken ct = default);
    }
}
