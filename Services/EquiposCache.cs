using CasaApuestas.Data.MySql;
using CasaApuestas.Models.Catalogo;
using Microsoft.Extensions.Caching.Memory;

namespace CasaApuestas.Services
{
    /// <summary>Datos del catalogo (MySQL) que cambian muy poco, servidos desde memoria unos minutos.</summary>
    public interface IEquiposCache
    {
        /// <summary>Equipos por id. Los que no estan en cache se piden a MySQL juntos, en UNA sola consulta IN.</summary>
        Task<IReadOnlyDictionary<int, Equipo>> ObtenerEquiposAsync(IEnumerable<int> ids, CancellationToken ct = default);

        Task<Liga?> ObtenerLigaAsync(int idLiga, CancellationToken ct = default);
    }

    /// <summary>
    /// Cache corto en memoria (3 minutos) del catalogo de equipos y ligas. Se guarda cada equipo por separado:
    /// asi, si el home necesita 12 equipos y 10 ya estan en cache, solo se consultan los 2 que faltan.
    /// Los PARTIDOS no se cachean: cambian a cada momento (en vivo, marcadores).
    /// </summary>
    public class EquiposCache : IEquiposCache
    {
        private static readonly TimeSpan Vida = TimeSpan.FromMinutes(3);

        private readonly ICatalogoRepository _catalogo;
        private readonly IMemoryCache _cache;

        public EquiposCache(ICatalogoRepository catalogo, IMemoryCache cache)
        {
            _catalogo = catalogo;
            _cache = cache;
        }

        public async Task<IReadOnlyDictionary<int, Equipo>> ObtenerEquiposAsync(IEnumerable<int> ids, CancellationToken ct = default)
        {
            var resultado = new Dictionary<int, Equipo>();
            var faltan = new List<int>();

            foreach (var id in ids.Distinct())
            {
                if (_cache.TryGetValue(ClaveEquipo(id), out Equipo? e) && e is not null) resultado[id] = e;
                else faltan.Add(id);
            }

            if (faltan.Count > 0)
            {
                foreach (var e in await _catalogo.ListarEquiposPorIdsAsync(faltan, ct))
                {
                    _cache.Set(ClaveEquipo(e.IdEquipo), e, Vida);
                    resultado[e.IdEquipo] = e;
                }
            }
            return resultado;
        }

        public async Task<Liga?> ObtenerLigaAsync(int idLiga, CancellationToken ct = default)
        {
            if (_cache.TryGetValue(ClaveLiga(idLiga), out Liga? liga) && liga is not null) return liga;
            liga = await _catalogo.ObtenerLigaAsync(idLiga, ct);
            if (liga is not null) _cache.Set(ClaveLiga(idLiga), liga, Vida);
            return liga;
        }

        private static string ClaveEquipo(int id) => $"catalogo:equipo:{id}";
        private static string ClaveLiga(int id) => $"catalogo:liga:{id}";
    }
}
