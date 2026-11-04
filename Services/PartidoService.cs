using CasaApuestas.Data.Postgres;
using CasaApuestas.Models.Catalogo;
using CasaApuestas.Models.Partidos;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Presentacion;

namespace CasaApuestas.Services
{
    public interface IPartidoService
    {
        Task<HomeViewModel> ObtenerHomeAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "home". AQUI se combinan dos gestores sin JOIN entre ellos:
    ///   1. PostgreSQL entrega los partidos (con ids de equipo) y sus cuotas.
    ///   2. Se reunen los ids de equipos que aparecen y se piden a MySQL en UNA sola consulta (con cache corto).
    ///   3. Con ambas respuestas se arman los ViewModels.
    /// El numero de consultas es fijo (no crece con el numero de partidos).
    /// </summary>
    public class PartidoService : IPartidoService
    {
        private static readonly string[] Codigos1X2 = { "LOCAL", "EMPATE", "VISITANTE" };

        private readonly IPartidoRepository _partidos;
        private readonly IEquiposCache _catalogo;
        private readonly TimeProvider _reloj;

        public PartidoService(IPartidoRepository partidos, IEquiposCache catalogo, TimeProvider reloj)
        {
            _partidos = partidos;
            _catalogo = catalogo;
            _reloj = reloj;
        }

        public async Task<HomeViewModel> ObtenerHomeAsync(CancellationToken ct = default)
        {
            var partidos = await _partidos.ListarPartidosActivosAsync(ct);
            if (partidos.Count == 0) return new HomeViewModel();

            var cuotas = await _partidos.ListarCuotasDePartidosAsync(partidos.Select(p => p.IdPartido).ToList(), ct);
            var calendarios = (await _partidos.ListarCalendariosAsync(partidos.Select(p => p.IdCalendario).Distinct().ToList(), ct))
                .ToDictionary(c => c.IdCalendario);

            // Una sola consulta a MySQL por todos los equipos del home (y solo por los que no estan en cache).
            var idsEquipos = partidos.SelectMany(p => new[] { p.IdEquipoLocal, p.IdEquipoVisitante }).Distinct().ToList();
            var equipos = await _catalogo.ObtenerEquiposAsync(idsEquipos, ct);

            var ligas = new Dictionary<int, Liga?>();
            foreach (var idLiga in calendarios.Values.Select(c => c.IdLiga).Distinct())
                ligas[idLiga] = await _catalogo.ObtenerLigaAsync(idLiga, ct);

            var mercados = (await _partidos.ListarMercadosAsync(ct)).ToDictionary(m => m.IdMercado);
            var ahora = FechaColombia.Ahora(_reloj);
            var cuotasPorPartido = cuotas.ToLookup(c => c.IdPartido);

            // Cada partido se asocia a su liga por su jornada (calendario); las filas se agrupan por liga.
            var filas = partidos.Select(p =>
            {
                var idLiga = calendarios.TryGetValue(p.IdCalendario, out var cal) ? cal.IdLiga : 0;
                ligas.TryGetValue(idLiga, out var liga);
                return (Titulo: TituloLiga(liga), Fila: ArmarFila(p, cuotasPorPartido[p.IdPartido].Where(c => p.Estado == "EN_VIVO" || c.Disponible).ToList(), equipos, mercados, ahora), EnVivo: p.Estado == "EN_VIVO");
            }).ToList();

            return new HomeViewModel
            {
                EnVivo = Agrupar(filas.Where(f => f.EnVivo).Select(f => (f.Titulo, f.Fila))),
                Proximos = Agrupar(filas.Where(f => !f.EnVivo).Select(f => (f.Titulo, f.Fila)))
            };
        }

        private static string TituloLiga(Liga? liga) =>
            liga is null ? "Liga" : $"{liga.Pais} / {liga.Nombre}";

        private static IReadOnlyList<GrupoLigaViewModel> Agrupar(IEnumerable<(string Titulo, PartidoFilaViewModel Fila)> filas) =>
            filas.GroupBy(f => f.Titulo)
                 .Select(g => new GrupoLigaViewModel(g.Key, g.Select(f => f.Fila).ToList()))
                 .ToList();

        private static PartidoFilaViewModel ArmarFila(Partido p, IReadOnlyList<Cuota> cuotas,
            IReadOnlyDictionary<int, Equipo> equipos, IReadOnlyDictionary<int, Mercado> mercados, DateTime ahora)
        {
            var enVivo = p.Estado == "EN_VIVO";
            var local = Mini(equipos, p.IdEquipoLocal, enVivo ? p.GolesLocal ?? 0 : null);
            var visitante = Mini(equipos, p.IdEquipoVisitante, enVivo ? p.GolesVisitante ?? 0 : null);

            // Mercado 1X2: se reconoce por el codigo estable de la seleccion, no por el nombre ni por un id fijo.
            var cuota1x2 = cuotas.Where(c => Codigos1X2.Contains(c.CodigoSeleccion)).ToList();
            CuotaViewModel Casilla(string etiqueta, string codigo)
            {
                var c = cuota1x2.FirstOrDefault(x => x.CodigoSeleccion == codigo);
                return new CuotaViewModel(etiqueta, c?.Valor, c?.IdCuota, c?.IdMercado, c?.Disponible ?? false);
            }
            var casillas = new[] { Casilla(local.Nombre, "LOCAL"), Casilla("Empate", "EMPATE"), Casilla(visitante.Nombre, "VISITANTE") };
            var idMercado1x2 = cuota1x2.Select(c => c.IdMercado).FirstOrDefault();
            var nombreMercado = mercados.TryGetValue(idMercado1x2, out var mk) ? mk.Nombre : "Resultado final 1X2";

            // "+N": mercados adicionales al 1X2 que tienen cuotas disponibles.
            var mercados1x2 = cuota1x2.Select(c => c.IdMercado).ToHashSet();
            var extra = cuotas.Select(c => c.IdMercado).Where(m => !mercados1x2.Contains(m)).Distinct().Count();

            return new PartidoFilaViewModel(p.IdPartido, enVivo, FechaColombia.Formatear(p.FechaHora, ahora), local, visitante, casillas, extra, nombreMercado, p.Minuto);
        }

        private static EquipoMiniViewModel Mini(IReadOnlyDictionary<int, Equipo> equipos, int idEquipo, int? goles)
        {
            // Si el equipo no esta en el catalogo (dato huerfano) no se rompe el home: se muestra un nombre generico.
            var nombre = equipos.TryGetValue(idEquipo, out var e) ? e.Nombre : $"Equipo {idEquipo}";
            return new EquipoMiniViewModel(nombre, Escudo.Iniciales(nombre), e?.EscudoUrl, goles);
        }
    }
}
