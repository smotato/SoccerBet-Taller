using CasaApuestas.Data.MySql;
using CasaApuestas.Data.Postgres;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Presentacion;

namespace CasaApuestas.Services
{
    public interface IPosicionesService
    {
        Task<PosicionesViewModel> ObtenerTablaAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "tabla de posiciones". La tabla NO se guarda: se calcula en cada consulta a partir de los partidos
    /// FINALIZADO (PostgreSQL), asi nunca queda desactualizada cuando el simulador finaliza un partido.
    /// Los equipos y la liga vienen de MySQL; se unen aqui, en la capa de servicios, sin JOIN entre gestores.
    /// </summary>
    public class PosicionesService : IPosicionesService
    {
        /// <summary>Los 8 primeros clasifican a cuadrangulares.</summary>
        public const int Cuadrangulares = 8;

        private const int PuntosVictoria = 3;
        private const int PuntosEmpate = 1;

        private readonly ICatalogoRepository _catalogo;
        private readonly IPartidoRepository _partidos;

        public PosicionesService(ICatalogoRepository catalogo, IPartidoRepository partidos)
        {
            _catalogo = catalogo;
            _partidos = partidos;
        }

        public async Task<PosicionesViewModel> ObtenerTablaAsync(CancellationToken ct = default)
        {
            var liga = await _catalogo.ObtenerLigaActivaAsync(ct);
            if (liga is null) return new PosicionesViewModel { Cuadrangulares = Cuadrangulares };

            var equipos = (await _catalogo.ListarEquiposAsync(ct)).Where(e => e.Activo && e.IdLiga == liga.IdLiga).ToList();
            var jugados = await _partidos.ListarPartidosFinalizadosAsync(liga.IdLiga, liga.Temporada, ct);

            // Acumuladores por equipo. Todos los equipos activos parten en ceros, asi los que no han jugado tambien aparecen.
            var acc = equipos.ToDictionary(e => e.IdEquipo, _ => new Acumulado());
            foreach (var p in jugados)
            {
                // Se ignoran partidos de equipos que no estan en el catalogo activo de la liga.
                if (!acc.TryGetValue(p.IdEquipoLocal, out var local) || !acc.TryGetValue(p.IdEquipoVisitante, out var visita)) continue;
                var gl = p.GolesLocal ?? 0;
                var gv = p.GolesVisitante ?? 0;
                local.Registrar(gl, gv);
                visita.Registrar(gv, gl);
            }

            // Desempate: puntos, diferencia de goles, goles a favor y, por ultimo, nombre (orden estable y predecible).
            var ordenados = equipos
                .Select(e => (Equipo: e, A: acc[e.IdEquipo]))
                .OrderByDescending(x => x.A.Puntos)
                .ThenByDescending(x => x.A.Diferencia)
                .ThenByDescending(x => x.A.GolesFavor)
                .ThenBy(x => x.Equipo.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var filas = ordenados.Select((x, i) => new FilaPosicionViewModel(
                i + 1, x.Equipo.IdEquipo, x.Equipo.Nombre, Escudo.Iniciales(x.Equipo.Nombre), x.Equipo.EscudoUrl,
                x.A.Jugados, x.A.Ganados, x.A.Empatados, x.A.Perdidos,
                x.A.GolesFavor, x.A.GolesContra, x.A.Diferencia, x.A.Puntos, i < Cuadrangulares)).ToList();

            return new PosicionesViewModel
            {
                LigaNombre = liga.Nombre,
                Pais = liga.Pais,
                Temporada = liga.Temporada,
                Cuadrangulares = Cuadrangulares,
                Filas = filas
            };
        }

        private sealed class Acumulado
        {
            public int Jugados, Ganados, Empatados, Perdidos, GolesFavor, GolesContra;
            public int Diferencia => GolesFavor - GolesContra;
            public int Puntos => Ganados * PuntosVictoria + Empatados * PuntosEmpate;

            public void Registrar(int golesPropios, int golesRival)
            {
                Jugados++;
                GolesFavor += golesPropios;
                GolesContra += golesRival;
                if (golesPropios > golesRival) Ganados++;
                else if (golesPropios == golesRival) Empatados++;
                else Perdidos++;
            }
        }
    }
}
