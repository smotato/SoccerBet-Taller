using CasaApuestas.Models.Catalogo;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.MySql
{
    /// <summary>
    /// Implementacion de ICatalogoRepository sobre MySQL. Todas las consultas usan AsNoTracking() (solo lectura)
    /// y NO usan Join ni propiedades de navegacion: equipo y estadio se unen en memoria, por IDs logicos.
    /// </summary>
    public class CatalogoRepository : ICatalogoRepository
    {
        private readonly CatalogoDbContext _db;

        public CatalogoRepository(CatalogoDbContext db) => _db = db;

        public async Task<Liga?> ObtenerLigaActivaAsync(CancellationToken ct = default) =>
            await _db.Ligas.AsNoTracking().Where(l => l.Activa).OrderBy(l => l.IdLiga).FirstOrDefaultAsync(ct);

        public async Task<Liga?> ObtenerLigaAsync(int idLiga, CancellationToken ct = default) =>
            await _db.Ligas.AsNoTracking().FirstOrDefaultAsync(l => l.IdLiga == idLiga, ct);

        public async Task<IReadOnlyList<Equipo>> ListarEquiposAsync(CancellationToken ct = default) =>
            await _db.Equipos.AsNoTracking().ToListAsync(ct);

        public async Task<Equipo?> ObtenerEquipoAsync(int idEquipo, CancellationToken ct = default) =>
            await _db.Equipos.AsNoTracking().FirstOrDefaultAsync(e => e.IdEquipo == idEquipo, ct);

        public async Task<IReadOnlyList<EstadioDeEquipo>> ListarEstadiosDeEquiposAsync(IReadOnlyCollection<int> idsEquipo, CancellationToken ct = default)
        {
            // Dos consultas sencillas y union en memoria (en lugar de un JOIN): es el mismo patron que se usara
            // entre gestores distintos y no depende de relaciones declaradas en el modelo.
            var enlaces = await _db.EquiposEstadios.AsNoTracking()
                .Where(x => idsEquipo.Contains(x.IdEquipo)).ToListAsync(ct);
            var idsEstadio = enlaces.Select(x => x.IdEstadio).Distinct().ToList();
            var estadios = await _db.Estadios.AsNoTracking()
                .Where(s => idsEstadio.Contains(s.IdEstadio)).ToDictionaryAsync(s => s.IdEstadio, ct);

            return enlaces
                .Where(x => estadios.ContainsKey(x.IdEstadio))
                .Select(x => new EstadioDeEquipo(x.IdEquipo, x.EsPrincipal, estadios[x.IdEstadio]))
                .ToList();
        }

        public async Task<IReadOnlyList<Jugador>> ListarJugadoresDeEquipoAsync(int idEquipo, CancellationToken ct = default) =>
            await _db.Jugadores.AsNoTracking().Where(j => j.IdEquipo == idEquipo).ToListAsync(ct);
    }
}
