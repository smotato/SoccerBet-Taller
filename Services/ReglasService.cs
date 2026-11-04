using CasaApuestas.Data.Mongo;
using CasaApuestas.Models.ViewModels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services
{
    public interface IReglasService
    {
        Task<ReglasViewModel> ObtenerAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "pagina de reglas" (RF8). Lee las reglas ACTIVAS de MongoDB y las reparte en dos secciones.
    /// Las reglas casi no cambian, asi que el resultado se guarda en memoria unos minutos (como EquiposCache): la mayoria
    /// de las visitas no consulta Mongo. Los fallos NO se guardan en cache, para que al volver Mongo la pagina se recupere
    /// de inmediato.
    /// </summary>
    public class ReglasService : IReglasService
    {
        private const string ClaveCache = "reglas:pagina";
        private const string CategoriaFutbol = "DEPORTE";
        private const string CategoriaApuestas = "APUESTAS";

        private readonly IReglaRepository _repo;
        private readonly IMemoryCache _cache;
        private readonly MongoOptions _opc;
        private readonly ILogger<ReglasService> _log;

        public ReglasService(IReglaRepository repo, IMemoryCache cache, IOptions<MongoOptions> opciones, ILogger<ReglasService> log)
        {
            _repo = repo;
            _cache = cache;
            _opc = opciones.Value;
            _log = log;
        }

        public async Task<ReglasViewModel> ObtenerAsync(CancellationToken ct = default)
        {
            if (_opc.CacheMinutos > 0 && _cache.TryGetValue(ClaveCache, out ReglasViewModel? guardado) && guardado is not null)
                return guardado;

            var docs = await _repo.ListarActivasAsync(ct);

            // El orden de las SECCIONES lo fija esta clase (futbol primero), no el orden alfabetico de la categoria, que
            // pondria APUESTAS antes que DEPORTE. Dentro de cada seccion: por "orden" y, si empata, por nombre.
            List<ReglaViewModel> De(string categoria) => docs
                .Where(d => string.Equals(d.Categoria, categoria, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Orden).ThenBy(d => d.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new ReglaViewModel(d.Orden, d.Nombre, d.Descripcion))
                .ToList();

            var futbol = De(CategoriaFutbol);
            var apuestas = De(CategoriaApuestas);
            var sinSeccion = docs.Count - futbol.Count - apuestas.Count;
            if (sinSeccion > 0)
                _log.LogWarning("{N} regla(s) activa(s) de MongoDB tienen una categoria desconocida y no se muestran.", sinSeccion);

            var vm = new ReglasViewModel
            {
                Futbol = new SeccionReglasViewModel("futbol", "Reglas del fútbol", futbol),
                Apuestas = new SeccionReglasViewModel("apuestas", "Reglas de apuestas", apuestas)
            };

            if (_opc.CacheMinutos > 0) _cache.Set(ClaveCache, vm, TimeSpan.FromMinutes(_opc.CacheMinutos));
            return vm;
        }
    }
}
