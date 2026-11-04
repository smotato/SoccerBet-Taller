using CasaApuestas.Models.Partidos;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.Postgres
{
    /// <summary>
    /// Implementacion sobre PostgreSQL. Lee las tablas (no la vista vw_cuotas_disponibles): asi un partido EN_VIVO
    /// sin cuotas disponibles igual aparece, y se tiene id_calendario para saber la liga. Todo con AsNoTracking().
    /// </summary>
    public class PartidoRepository : IPartidoRepository
    {
        private static readonly string[] EstadosVisibles = { "EN_VIVO", "PROGRAMADO" };

        private readonly PartidosDbContext _db;

        public PartidoRepository(PartidosDbContext db) => _db = db;

        public async Task<IReadOnlyList<Partido>> ListarPartidosActivosAsync(CancellationToken ct = default) =>
            await _db.Partidos.AsNoTracking()
                .Where(p => EstadosVisibles.Contains(p.Estado))
                .OrderBy(p => p.FechaHora).ThenBy(p => p.IdPartido)
                .ToListAsync(ct);

        public async Task<IReadOnlyList<Cuota>> ListarCuotasDePartidosAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default)
        {
            if (idsPartido.Count == 0) return Array.Empty<Cuota>();
            return await _db.Cuotas.AsNoTracking()
                .Where(c => idsPartido.Contains(c.IdPartido) && _db.Mercados.Any(m => m.IdMercado == c.IdMercado && m.Activo))
                .OrderBy(c => c.IdPartido).ThenBy(c => c.IdMercado).ThenBy(c => c.IdCuota)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Cuota>> ListarCuotasDisponiblesAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default)
        {
            if (idsPartido.Count == 0) return Array.Empty<Cuota>();

            // Una consulta con WHERE id_partido IN (...). El EXISTS sobre mercado es dentro del mismo gestor
            // (no es un JOIN entre gestores) y descarta mercados desactivados.
            return await _db.Cuotas.AsNoTracking()
                .Where(c => idsPartido.Contains(c.IdPartido) && c.Disponible
                            && _db.Mercados.Any(m => m.IdMercado == c.IdMercado && m.Activo))
                .OrderBy(c => c.IdPartido).ThenBy(c => c.IdMercado).ThenBy(c => c.IdCuota)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Partido>> ListarPartidosFinalizadosAsync(int idLiga, string temporada, CancellationToken ct = default) =>
            await _db.Partidos.AsNoTracking()
                .Where(p => p.Estado == "FINALIZADO" && p.GolesLocal != null && p.GolesVisitante != null
                            && _db.Calendarios.Any(c => c.IdCalendario == p.IdCalendario && c.IdLiga == idLiga && c.Temporada == temporada))
                .ToListAsync(ct);

        public async Task<Partido?> ObtenerPartidoAsync(int idPartido, CancellationToken ct = default) =>
            await _db.Partidos.AsNoTracking().FirstOrDefaultAsync(p => p.IdPartido == idPartido, ct);

        public async Task<IReadOnlyList<Cuota>> ListarCuotasDePartidoAsync(int idPartido, CancellationToken ct = default) =>
            await _db.Cuotas.AsNoTracking().Where(c => c.IdPartido == idPartido)
                .OrderBy(c => c.IdMercado).ThenBy(c => c.IdCuota).ToListAsync(ct);

        public async Task<IReadOnlyList<Cuota>> ListarCuotasPorIdsAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default)
        {
            if (idsCuota.Count == 0) return Array.Empty<Cuota>();
            return await _db.Cuotas.AsNoTracking().Where(c => idsCuota.Contains(c.IdCuota)).ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Partido>> ListarPartidosPorIdsAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default)
        {
            if (idsPartido.Count == 0) return Array.Empty<Partido>();
            return await _db.Partidos.AsNoTracking().Where(p => idsPartido.Contains(p.IdPartido)).ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Mercado>> ListarMercadosAsync(CancellationToken ct = default) =>
            await _db.Mercados.AsNoTracking().OrderBy(m => m.IdMercado).ToListAsync(ct);

        public async Task<IReadOnlyList<Calendario>> ListarCalendariosAsync(IReadOnlyCollection<int> idsCalendario, CancellationToken ct = default)
        {
            if (idsCalendario.Count == 0) return Array.Empty<Calendario>();
            return await _db.Calendarios.AsNoTracking()
                .Where(c => idsCalendario.Contains(c.IdCalendario)).ToListAsync(ct);
        }
    }
}
