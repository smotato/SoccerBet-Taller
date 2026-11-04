using CasaApuestas.Data.MySql;
using CasaApuestas.Data.Postgres;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services.Simulador
{
    public interface IGeneradorJornada
    {
        /// <summary>
        /// Si no queda ningun partido PROGRAMADO, crea la siguiente jornada. Devuelve true si la creo esta llamada.
        /// Es segura de llamar en cada ciclo: con partidos programados no hace nada, y si dos instancias coinciden solo una crea.
        /// </summary>
        Task<bool> CrearSiNoHayProgramadosAsync(DateTime ahoraColombia, CancellationToken ct = default);
    }

    /// <summary>
    /// Mantiene la demo viva: cuando se acaban los partidos programados genera la jornada siguiente.
    ///   - Emparejamientos: round-robin ("metodo del circulo") de los equipos activos de la liga (MySQL): en 19 jornadas todos
    ///     juegan contra todos; si la jornada numero N ya existe, se sigue con la N+1.
    ///   - Estadio: el principal del equipo local (MySQL).
    ///   - Horas: el primer partido a MinutosHastaPrimerPartido de ahora y los demas cada SeparacionPartidosMinutos.
    ///   - Cuotas: los 3 mercados (7 cuotas) calculadas con un modelo de Poisson a partir de la "fuerza" de cada equipo y el margen
    ///     de la casa; cuota_inicial = cuota.
    /// Todo se inserta en UNA transaccion; el UNIQUE (liga, temporada, jornada) de calendario evita duplicar una jornada aunque
    /// dos instancias lo intenten a la vez.
    /// </summary>
    public sealed class GeneradorJornada : IGeneradorJornada
    {
        private const decimal CuotaMinima = 1.05m;

        private readonly ISimuladorRepository _repo;
        private readonly IServiceScopeFactory _scopes;
        private readonly SimuladorOptions _opc;
        private readonly ILogger<GeneradorJornada> _log;

        public GeneradorJornada(ISimuladorRepository repo, IServiceScopeFactory scopes, IOptions<SimuladorOptions> opciones, ILogger<GeneradorJornada> log)
        {
            _repo = repo;
            _scopes = scopes;
            _opc = opciones.Value;
            _log = log;
        }

        public async Task<bool> CrearSiNoHayProgramadosAsync(DateTime ahora, CancellationToken ct = default)
        {
            if (await _repo.ContarProgramadosAsync(ct) > 0) return false;

            var ultima = await _repo.ObtenerUltimaJornadaAsync(ct);
            if (ultima is null)
            {
                _log.LogWarning("No hay ninguna jornada previa: no se puede crear la siguiente.");
                return false;
            }

            // ---- Catalogo (MySQL): equipos activos de la liga, sus nombres y su estadio principal ----
            List<Models.Catalogo.Equipo> equipos;
            IReadOnlyList<EstadioDeEquipo> estadios;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                var catalogo = scope.ServiceProvider.GetRequiredService<ICatalogoRepository>();
                equipos = (await catalogo.ListarEquiposAsync(ct)).Where(e => e.Activo && e.IdLiga == ultima.IdLiga).OrderBy(e => e.IdEquipo).ToList();
                estadios = await catalogo.ListarEstadiosDeEquiposAsync(equipos.Select(e => e.IdEquipo).ToList(), ct);
            }
            if (equipos.Count < 2)
            {
                _log.LogWarning("La liga {IdLiga} tiene menos de 2 equipos activos: no se crea jornada.", ultima.IdLiga);
                return false;
            }

            // ---- Mercados (PostgreSQL), reconocidos por nombre ----
            var mercados = await _repo.ListarMercadosAsync(ct);
            int? m1x2 = mercados.FirstOrDefault(m => m.Nombre.Contains("1X2", StringComparison.OrdinalIgnoreCase))?.IdMercado;
            int? mTotal = mercados.FirstOrDefault(m => m.Nombre.Contains("Total", StringComparison.OrdinalIgnoreCase))?.IdMercado;
            int? mAmbos = mercados.FirstOrDefault(m => m.Nombre.Contains("Ambos", StringComparison.OrdinalIgnoreCase))?.IdMercado;
            if (m1x2 is null || mTotal is null || mAmbos is null)
            {
                _log.LogError("No se encontraron los 3 mercados (1X2, Total, Ambos) en PostgreSQL: no se crea jornada.");
                return false;
            }

            var estadioPrincipal = estadios.GroupBy(x => x.IdEquipo)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EsPrincipal).ThenBy(x => x.Estadio.IdEstadio).First().Estadio.IdEstadio);
            var estadioRespaldo = estadios.Select(x => x.Estadio.IdEstadio).DefaultIfEmpty(0).Min();
            if (estadioRespaldo == 0)
            {
                _log.LogError("Ningun equipo tiene estadio en el catalogo: no se crea jornada.");
                return false;
            }
            var nombres = equipos.ToDictionary(e => e.IdEquipo, e => e.Nombre);

            // ---- Emparejamientos, horas y cuotas ----
            var numero = ultima.Numero + 1;
            var primera = ahora.AddMinutes(_opc.MinutosHastaPrimerPartido);
            var pares = Emparejar(equipos.Select(e => e.IdEquipo).ToList(), numero);
            var partidos = new List<NuevoPartido>();
            for (var i = 0; i < pares.Count; i++)
            {
                var (local, visitante) = pares[i];
                var (el, ev) = ModeloCuotas.GolesEsperadosPorFuerza(local, visitante, _opc.GolesEsperadosPorPartido);
                var pr = ModeloCuotas.Prepartido(el, ev);
                var c = (double p) => ModeloCuotas.Cuota(p, _opc.MargenCasa, CuotaMinima);
                var cuotas = new List<NuevaCuota>
                {
                    new(m1x2.Value, $"Gana {nombres[local]}", "LOCAL", c(pr.Local)),
                    new(m1x2.Value, "Empate", "EMPATE", c(pr.Empate)),
                    new(m1x2.Value, $"Gana {nombres[visitante]}", "VISITANTE", c(pr.Visitante)),
                    new(mTotal.Value, "Más de 2.5", "MAS_2_5", c(pr.Mas25)),
                    new(mTotal.Value, "Menos de 2.5", "MENOS_2_5", c(pr.Menos25)),
                    new(mAmbos.Value, "Sí", "SI", c(pr.AmbosSi)),
                    new(mAmbos.Value, "No", "NO", c(pr.AmbosNo)),
                };
                var fecha = primera.AddMinutes(i * _opc.SeparacionPartidosMinutos);
                partidos.Add(new NuevoPartido(estadioPrincipal.GetValueOrDefault(local, estadioRespaldo), local, visitante,
                    new DateTime(fecha.Year, fecha.Month, fecha.Day, fecha.Hour, fecha.Minute, fecha.Second, DateTimeKind.Unspecified), cuotas));
            }

            var jornada = new NuevaJornada(ultima.IdLiga, ultima.Temporada, numero, partidos[0].FechaHora, partidos[^1].FechaHora, partidos);
            var ids = await _repo.CrearJornadaAsync(jornada, ct);
            if (ids is null)
            {
                _log.LogInformation("La jornada {Numero} ya habia sido creada por otra instancia.", numero);
                return false;
            }

            _log.LogInformation("JORNADA {Numero} creada ({Temporada}): {Partidos} partidos ({Cuotas} cuotas), el primero a las {Primero:HH:mm} y el ultimo a las {Ultimo:HH:mm}.",
                numero, ultima.Temporada, ids.Count, ids.Count * 7, partidos[0].FechaHora, partidos[^1].FechaHora);
            return true;
        }

        /// <summary>
        /// Metodo del circulo: el primer equipo queda fijo y los demas rotan una posicion por jornada. La ronda sale del numero de
        /// jornada, asi que es determinista (la misma jornada siempre tiene los mismos partidos). Local y visitante se alternan.
        /// </summary>
        public static List<(int Local, int Visitante)> Emparejar(IReadOnlyList<int> equipos, int numeroJornada)
        {
            var lista = equipos.Select(e => (int?)e).ToList();
            if (lista.Count % 2 == 1) lista.Add(null);                 // descansa uno (jornada con numero impar de equipos)

            var n = lista.Count;
            var ronda = (numeroJornada - 1) % (n - 1);
            var resto = lista.Skip(1).ToList();
            var rotado = new List<int?> { lista[0] };
            for (var i = 0; i < resto.Count; i++) rotado.Add(resto[(i + resto.Count - ronda) % resto.Count]);

            var pares = new List<(int, int)>();
            for (var i = 0; i < n / 2; i++)
            {
                var a = rotado[i];
                var b = rotado[n - 1 - i];
                if (a is null || b is null) continue;
                // Local/visitante: el equipo fijo alterna por ronda y los demas segun su posicion en la lista. (Alternar por
                // posicion + ronda NO sirve: los equipos rotan una posicion por ronda y la suma no cambia, asi que un equipo
                // quedaria siempre de local o siempre de visitante.) Con esta regla cada equipo juega 9 o 10 de 19 en casa.
                var localEsPrimero = i == 0 ? ronda % 2 == 0 : i % 2 == 0;
                pares.Add(localEsPrimero ? (a.Value, b.Value) : (b.Value, a.Value));
            }
            return pares;
        }
    }
}
