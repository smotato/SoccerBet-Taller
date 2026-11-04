using CasaApuestas.Data.MySql;
using CasaApuestas.Models.Catalogo;
using Microsoft.Extensions.Caching.Memory;

namespace CasaApuestas.Services.Simulador
{
    public interface IJugadoresCache
    {
        /// <summary>Plantilla de un equipo (catalogo MySQL), guardada en memoria 30 minutos.</summary>
        Task<IReadOnlyList<Jugador>> ObtenerAsync(int idEquipo, CancellationToken ct = default);
    }

    /// <summary>
    /// El simulador es un servicio de larga vida (singleton) pero el repositorio de MySQL usa un DbContext por peticion
    /// (scoped): por eso aqui se crea un "scope" propio solo cuando hay que consultar. La plantilla casi no cambia, asi que
    /// se cachea.
    /// </summary>
    public class JugadoresCache : IJugadoresCache
    {
        private static readonly TimeSpan Vida = TimeSpan.FromMinutes(30);

        private readonly IServiceScopeFactory _scopes;
        private readonly IMemoryCache _cache;

        public JugadoresCache(IServiceScopeFactory scopes, IMemoryCache cache)
        {
            _scopes = scopes;
            _cache = cache;
        }

        public async Task<IReadOnlyList<Jugador>> ObtenerAsync(int idEquipo, CancellationToken ct = default)
        {
            var clave = $"catalogo:jugadores:{idEquipo}";
            if (_cache.TryGetValue(clave, out IReadOnlyList<Jugador>? guardados) && guardados is not null) return guardados;

            await using var scope = _scopes.CreateAsyncScope();
            var jugadores = await scope.ServiceProvider.GetRequiredService<ICatalogoRepository>().ListarJugadoresDeEquipoAsync(idEquipo, ct);
            _cache.Set(clave, jugadores, Vida);
            return jugadores;
        }
    }
}
