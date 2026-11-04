using CasaApuestas.Data.Postgres;

namespace CasaApuestas.Services
{
    public record EstadoCuotaDto(int IdCuota, decimal Cuota, bool Disponible);

    /// <summary>Lo que la pagina necesita para refrescar un partido: estado, minuto, marcador y cuotas actuales.</summary>
    public record EstadoPartidoDto(int Id, string Estado, int? Minuto, int? GolesLocal, int? GolesVisitante, IReadOnlyList<EstadoCuotaDto> Cuotas);

    public interface IEstadoVivoService
    {
        Task<IReadOnlyList<EstadoPartidoDto>> ObtenerAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default);
    }

    /// <summary>
    /// Estado en vivo de varios partidos con DOS consultas a PostgreSQL (partidos y cuotas por id), sin importar cuantos sean.
    /// Es de solo lectura y no necesita MySQL: la pagina ya tiene los nombres; aqui solo viajan numeros.
    /// </summary>
    public class EstadoVivoService : IEstadoVivoService
    {
        private readonly IPartidoRepository _partidos;

        public EstadoVivoService(IPartidoRepository partidos) => _partidos = partidos;

        public async Task<IReadOnlyList<EstadoPartidoDto>> ObtenerAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        {
            var partidos = await _partidos.ListarPartidosPorIdsAsync(ids, ct);
            var cuotas = (await _partidos.ListarCuotasDePartidosAsync(ids, ct)).ToLookup(c => c.IdPartido);

            return partidos.OrderBy(p => p.IdPartido).Select(p => new EstadoPartidoDto(
                p.IdPartido, p.Estado, p.Minuto, p.GolesLocal, p.GolesVisitante,
                cuotas[p.IdPartido].Select(c => new EstadoCuotaDto(c.IdCuota, c.Valor, c.Disponible)).ToList())).ToList();
        }
    }
}
