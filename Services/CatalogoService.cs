using CasaApuestas.Data.MySql;
using CasaApuestas.Models.Catalogo;
using CasaApuestas.Models.ViewModels;

namespace CasaApuestas.Services
{
    public interface ICatalogoService
    {
        Task<EquiposListaViewModel> ListarEquiposAsync(CancellationToken ct = default);

        /// <summary>Devuelve null si el equipo no existe (o esta inactivo).</summary>
        Task<EquipoDetalleViewModel?> ObtenerDetalleEquipoAsync(int idEquipo, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "catalogo de equipos y plantillas". Decide QUE se muestra y en que orden; el repositorio solo lee.
    /// Si en el futuro estos datos salen de otra fuente (otro gestor o una API), solo cambia el repositorio.
    /// </summary>
    public class CatalogoService : ICatalogoService
    {
        // Orden y etiqueta de los grupos de la plantilla. La clave es el valor guardado en jugador.posicion.
        private static readonly (string Clave, string Titulo)[] Posiciones =
        {
            ("PORTERO", "Porteros"),
            ("DEFENSA", "Defensas"),
            ("MEDIOCAMPISTA", "Mediocampistas"),
            ("DELANTERO", "Delanteros"),
        };

        private readonly ICatalogoRepository _repo;

        public CatalogoService(ICatalogoRepository repo) => _repo = repo;

        public async Task<EquiposListaViewModel> ListarEquiposAsync(CancellationToken ct = default)
        {
            var equipos = (await _repo.ListarEquiposAsync(ct))
                .Where(e => e.Activo)
                .OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var liga = await _repo.ObtenerLigaActivaAsync(ct);
            var estadios = await _repo.ListarEstadiosDeEquiposAsync(equipos.Select(e => e.IdEquipo).ToList(), ct);

            var tarjetas = equipos.Select(e => new EquipoCardViewModel(
                e.IdEquipo, e.Nombre, Iniciales(e.Nombre), e.EscudoUrl, e.Ciudad,
                EstadioPrincipal(estadios, e.IdEquipo)?.Estadio.Nombre)).ToList();

            return new EquiposListaViewModel
            {
                LigaNombre = liga?.Nombre,
                Temporada = liga?.Temporada,
                Pais = liga?.Pais,
                Equipos = tarjetas
            };
        }

        public async Task<EquipoDetalleViewModel?> ObtenerDetalleEquipoAsync(int idEquipo, CancellationToken ct = default)
        {
            var equipo = await _repo.ObtenerEquipoAsync(idEquipo, ct);
            if (equipo is null || !equipo.Activo) return null;

            var liga = await _repo.ObtenerLigaAsync(equipo.IdLiga, ct);
            var estadios = await _repo.ListarEstadiosDeEquiposAsync(new[] { equipo.IdEquipo }, ct);
            var jugadores = await _repo.ListarJugadoresDeEquipoAsync(equipo.IdEquipo, ct);
            var estadio = EstadioPrincipal(estadios, equipo.IdEquipo)?.Estadio;

            return new EquipoDetalleViewModel
            {
                IdEquipo = equipo.IdEquipo,
                Nombre = equipo.Nombre,
                Iniciales = Iniciales(equipo.Nombre),
                EscudoUrl = equipo.EscudoUrl,
                Ciudad = equipo.Ciudad,
                Pais = liga?.Pais ?? estadio?.Pais,
                EstadioNombre = estadio?.Nombre,
                EstadioCapacidad = estadio?.Capacidad,
                LigaNombre = liga?.Nombre,
                Temporada = liga?.Temporada,
                Grupos = AgruparPlantilla(jugadores)
            };
        }

        /// <summary>El estadio marcado es_principal; si ninguno lo esta, el primero que tenga el equipo.</summary>
        private static EstadioDeEquipo? EstadioPrincipal(IReadOnlyList<EstadioDeEquipo> estadios, int idEquipo) =>
            estadios.Where(x => x.IdEquipo == idEquipo)
                    .OrderByDescending(x => x.EsPrincipal)
                    .ThenBy(x => x.Estadio.IdEstadio)
                    .FirstOrDefault();

        /// <summary>
        /// Porteros, defensas, mediocampistas y delanteros (solo los grupos que tengan jugadores) y, al final,
        /// los de posicion NULL en un bloque "Plantilla". Las entidades no se mutan: se proyectan a ViewModels.
        /// </summary>
        private static IReadOnlyList<GrupoPlantillaViewModel> AgruparPlantilla(IReadOnlyList<Jugador> jugadores)
        {
            var grupos = new List<GrupoPlantillaViewModel>();

            foreach (var (clave, titulo) in Posiciones)
            {
                var delGrupo = jugadores.Where(j => string.Equals(j.Posicion, clave, StringComparison.OrdinalIgnoreCase)).ToList();
                if (delGrupo.Count > 0)
                    grupos.Add(new GrupoPlantillaViewModel(titulo, false, Ordenar(delGrupo)));
            }

            var sinPosicion = jugadores.Where(j => !Posiciones.Any(p => string.Equals(j.Posicion, p.Clave, StringComparison.OrdinalIgnoreCase))).ToList();
            if (sinPosicion.Count > 0)
                grupos.Add(new GrupoPlantillaViewModel("Plantilla", true, Ordenar(sinPosicion)));

            return grupos;
        }

        /// <summary>
        /// Por numero de camiseta ascendente con los NULL AL FINAL (en MySQL un ORDER BY ascendente pone los NULL primero,
        /// por eso se ordena aqui y no en la consulta), y despues por apellidos y nombres.
        /// </summary>
        private static IReadOnlyList<JugadorFilaViewModel> Ordenar(IEnumerable<Jugador> jugadores) =>
            jugadores
                .OrderBy(j => j.NumeroCamiseta.HasValue ? 0 : 1)
                .ThenBy(j => j.NumeroCamiseta)
                .ThenBy(j => j.Apellidos, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(j => j.Nombres, StringComparer.CurrentCultureIgnoreCase)
                .Select(j => new JugadorFilaViewModel(j.NumeroCamiseta, $"{j.Nombres} {j.Apellidos}".Trim(), j.Nacionalidad))
                .ToList();

        private static string Iniciales(string nombre) => Presentacion.Escudo.Iniciales(nombre);
    }
}
