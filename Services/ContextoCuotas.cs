using CasaApuestas.Data.Postgres;
using CasaApuestas.Models.Partidos;
using CasaApuestas.Services.Presentacion;

namespace CasaApuestas.Services
{
    /// <summary>
    /// Datos de un grupo de cuotas ya leidos: la cuota, su partido y su mercado (PostgreSQL) y los nombres de los equipos
    /// (MySQL). Sirve para validar un cupon (ApuestaService) y para mostrar las selecciones de Mis apuestas.
    /// </summary>
    public sealed class ContextoCuotas
    {
        public static readonly ContextoCuotas Vacio = new(new(), new(), new(), new());

        public Dictionary<int, Cuota> Cuotas { get; }
        public Dictionary<int, Partido> Partidos { get; }
        public Dictionary<int, Mercado> Mercados { get; }
        public Dictionary<int, (string Local, string Visitante)> Equipos { get; }

        public ContextoCuotas(Dictionary<int, Cuota> cuotas, Dictionary<int, Partido> partidos, Dictionary<int, Mercado> mercados,
            Dictionary<int, (string Local, string Visitante)> equiposPorPartido)
        {
            Cuotas = cuotas; Partidos = partidos; Mercados = mercados; Equipos = equiposPorPartido;
        }

        /// <summary>Textos para mostrar. Si faltan los nombres de equipos (MySQL caido) se usan textos genericos.</summary>
        public (string Partido, string Mercado, string Seleccion) Textos(Cuota c)
        {
            var hay = Equipos.TryGetValue(c.IdPartido, out var e);
            var local = hay ? e.Local : "Local";
            var visita = hay ? e.Visitante : "Visitante";
            var partido = hay ? $"{local} vs {visita}" : $"Partido #{c.IdPartido}";
            var mercado = Mercados.TryGetValue(c.IdMercado, out var m) ? m.Nombre : $"Mercado #{c.IdMercado}";
            return (partido, mercado, EtiquetaCuota.Texto(c, local, visita));
        }
    }

    public interface IContextoCuotasLoader
    {
        /// <summary>
        /// Lee las cuotas indicadas y todo lo necesario para describirlas. PostgreSQL es obligatorio (si esta caido lanza
        /// la excepcion); MySQL solo aporta nombres y, si falla, el contexto sigue sin ellos.
        /// </summary>
        Task<ContextoCuotas> CargarAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default);
    }

    /// <summary>
    /// Una consulta por tabla con WHERE ... IN (...) (cuotas, partidos), los mercados (3 filas) y los equipos desde el
    /// cache; el numero de consultas no crece con el numero de apuestas ni de selecciones. Sin JOIN entre gestores.
    /// </summary>
    public class ContextoCuotasLoader : IContextoCuotasLoader
    {
        private readonly IPartidoRepository _partidos;
        private readonly IEquiposCache _catalogo;
        private readonly ILogger<ContextoCuotasLoader> _log;

        public ContextoCuotasLoader(IPartidoRepository partidos, IEquiposCache catalogo, ILogger<ContextoCuotasLoader> log)
        {
            _partidos = partidos;
            _catalogo = catalogo;
            _log = log;
        }

        public async Task<ContextoCuotas> CargarAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default)
        {
            var cuotas = (await _partidos.ListarCuotasPorIdsAsync(idsCuota, ct)).ToDictionary(c => c.IdCuota);
            var partidos = (await _partidos.ListarPartidosPorIdsAsync(cuotas.Values.Select(c => c.IdPartido).Distinct().ToList(), ct))
                .ToDictionary(p => p.IdPartido);
            var mercados = (await _partidos.ListarMercadosAsync(ct)).ToDictionary(m => m.IdMercado);

            var equiposPorPartido = new Dictionary<int, (string Local, string Visitante)>();
            try
            {
                var nombres = await _catalogo.ObtenerEquiposAsync(partidos.Values.SelectMany(p => new[] { p.IdEquipoLocal, p.IdEquipoVisitante }), ct);
                foreach (var p in partidos.Values)
                    if (nombres.TryGetValue(p.IdEquipoLocal, out var l) && nombres.TryGetValue(p.IdEquipoVisitante, out var v))
                        equiposPorPartido[p.IdPartido] = (l.Nombre, v.Nombre);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudieron leer los nombres de los equipos (MySQL); se usaran textos genericos.");
            }
            return new ContextoCuotas(cuotas, partidos, mercados, equiposPorPartido);
        }
    }
}
