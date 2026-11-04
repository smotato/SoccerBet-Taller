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

        public async Task<IReadOnlyList<Calendario>> ListarCalendariosAsync(IReadOnlyCollection<int> idsCalendario, CancellationToken ct = default)
        {
            if (idsCalendario.Count == 0) return Array.Empty<Calendario>();
            return await _db.Calendarios.AsNoTracking()
                .Where(c => idsCalendario.Contains(c.IdCalendario)).ToListAsync(ct);
        }
    }
}
