using CasaApuestas.Data.Mongo;
using CasaApuestas.Data.Postgres;
using CasaApuestas.Models.Catalogo;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Simulador;

namespace CasaApuestas.Services
{
    public interface IEventosPartidoService
    {
        /// <summary>Con los datos del partido ya conocidos (los usa el detalle). Nunca lanza por un fallo de Mongo.</summary>
        Task<EventosPartidoViewModel> ObtenerAsync(int idPartido, string estado, int idLocal, int idVisitante, string nombreLocal, string nombreVisitante, CancellationToken ct = default);

        /// <summary>Para el fragmento de refresco: busca el partido primero. null si el partido no existe.</summary>
        Task<EventosPartidoViewModel?> ObtenerPorIdAsync(int idPartido, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "eventos y alineaciones" (RF7). MongoDB entrega eventos y alineaciones (solo ids); los NOMBRES de jugadores y
    /// equipos salen del catalogo de MySQL (caches existentes) y se unen aqui, sin JOIN entre gestores. El texto `descripcion` que
    /// guarda el simulador en Mongo se IGNORA al leer. Si Mongo falla, se devuelve un modelo con MongoNoDisponible y el detalle
    /// (cuotas incluidas) sigue funcionando.
    /// </summary>
    public class EventosPartidoService : IEventosPartidoService
    {
        private readonly IEventoPartidoRepository _eventos;
        private readonly IAlineacionRepository _alineaciones;
        private readonly IJugadoresCache _jugadores;
        private readonly IPartidoRepository _partidos;
        private readonly IEquiposCache _catalogo;
        private readonly ILogger<EventosPartidoService> _log;

        public EventosPartidoService(IEventoPartidoRepository eventos, IAlineacionRepository alineaciones, IJugadoresCache jugadores,
            IPartidoRepository partidos, IEquiposCache catalogo, ILogger<EventosPartidoService> log)
        {
            _eventos = eventos;
            _alineaciones = alineaciones;
            _jugadores = jugadores;
            _partidos = partidos;
            _catalogo = catalogo;
            _log = log;
        }

        public async Task<EventosPartidoViewModel?> ObtenerPorIdAsync(int idPartido, CancellationToken ct = default)
        {
            var partido = await _partidos.ObtenerPartidoAsync(idPartido, ct);
            if (partido is null) return null;
            var equipos = await _catalogo.ObtenerEquiposAsync(new[] { partido.IdEquipoLocal, partido.IdEquipoVisitante }, ct);
            string Nombre(int id) => equipos.TryGetValue(id, out var e) ? e.Nombre : $"Equipo {id}";
            return await ObtenerAsync(idPartido, partido.Estado, partido.IdEquipoLocal, partido.IdEquipoVisitante, Nombre(partido.IdEquipoLocal), Nombre(partido.IdEquipoVisitante), ct);
        }

        public async Task<EventosPartidoViewModel> ObtenerAsync(int idPartido, string estado, int idLocal, int idVisitante,
            string nombreLocal, string nombreVisitante, CancellationToken ct = default)
        {
            // Un partido anulado no muestra cronologia ni alineacion: solo el aviso (no se consulta Mongo).
            if (estado == "SUSPENDIDO")
                return new EventosPartidoViewModel { IdPartido = idPartido, Estado = estado, MensajeEstado = "Partido anulado." };

            IReadOnlyList<EventoPartidoDocumento> eventos;
            IReadOnlyList<AlineacionDocumento> alineaciones;
            try
            {
                eventos = estado == "PROGRAMADO" ? Array.Empty<EventoPartidoDocumento>() : await _eventos.ListarPorPartidoAsync(idPartido, ct);
                alineaciones = await _alineaciones.ObtenerPorPartidoAsync(idPartido, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudieron leer los eventos/alineaciones del partido {IdPartido} en MongoDB.", idPartido);
                return new EventosPartidoViewModel { IdPartido = idPartido, Estado = estado, MongoNoDisponible = true };
            }

            // Nombres: catalogo de MySQL (cache de 30 min). Si no responde, se usa un texto neutro, nunca el de Mongo.
            var nombres = new Dictionary<int, string>();
            try
            {
                foreach (var idEquipo in new[] { idLocal, idVisitante }.Distinct())
                    foreach (var j in await _jugadores.ObtenerAsync(idEquipo, ct))
                        nombres[j.IdJugador] = $"{j.Nombres} {j.Apellidos}".Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudieron leer los nombres de jugadores del partido {IdPartido} (MySQL).", idPartido);
            }
            string Jugador(int? id) => id is int i && nombres.TryGetValue(i, out var n) ? n : "Jugador no identificado";
            string Equipo(int? id) => id == idLocal ? nombreLocal : id == idVisitante ? nombreVisitante : "";

            // Mas reciente arriba (como el mockup): minuto descendente; en el mismo minuto, el ultimo registrado primero.
            var filas = eventos
                .OrderByDescending(e => e.Minuto).ThenByDescending(e => e.MinutoAdicional ?? 0).ThenByDescending(e => e.Creado)
                .Select(e => e.TipoEvento switch
                {
                    "GOL" => new EventoFilaViewModel(Min(e), "GOL", $"Gol de {Jugador(e.IdJugador)} ({Equipo(e.IdEquipo)})"),
                    "TARJETA_AMARILLA" => new EventoFilaViewModel(Min(e), "AMARILLA", $"Tarjeta amarilla · {Jugador(e.IdJugador)} ({Equipo(e.IdEquipo)})"),
                    "TARJETA_ROJA" => new EventoFilaViewModel(Min(e), "ROJA", $"Tarjeta roja · {Jugador(e.IdJugador)} ({Equipo(e.IdEquipo)})"),
                    "INICIO" => new EventoFilaViewModel(Min(e), "INICIO", "Comienza el partido"),
                    "FIN" => new EventoFilaViewModel(Min(e), "FIN", "Final del partido"),
                    _ => null
                })
                .Where(f => f is not null).Select(f => f!).ToList();

            // Alineaciones: local primero, luego visitante. Solo titulares (INICIAL).
            var equipos = new[] { (idLocal, nombreLocal), (idVisitante, nombreVisitante) };
            var vmAlineaciones = new List<AlineacionEquipoViewModel>();
            foreach (var (idEquipo, nombre) in equipos)
            {
                var doc = alineaciones.FirstOrDefault(a => a.IdEquipo == idEquipo && a.Tipo == AlineacionDocumento.TipoInicial);
                if (doc is null) continue;
                vmAlineaciones.Add(new AlineacionEquipoViewModel(nombre, doc.Formacion,
                    doc.Jugadores.Where(j => j.Titular).OrderBy(j => OrdenPosicion(j.Posicion)).ThenBy(j => j.NumeroCamiseta ?? int.MaxValue)
                        .Select(j => new JugadorAlineacionViewModel(j.NumeroCamiseta, Jugador(j.IdJugador), j.Posicion)).ToList()));
            }

            string? mensaje = null;
            if (filas.Count == 0)
                mensaje = estado == "FINALIZADO" ? "No hay eventos registrados." : "Aún no hay eventos.";

            return new EventosPartidoViewModel { IdPartido = idPartido, Estado = estado, MensajeEstado = mensaje, Eventos = filas, Alineaciones = vmAlineaciones };
        }

        private static string Min(EventoPartidoDocumento e) => e.MinutoAdicional is int a and > 0 ? $"{e.Minuto}+{a}'" : $"{e.Minuto}'";

        private static int OrdenPosicion(string? p) => (p ?? "").ToUpperInvariant() switch
        {
            "PORTERO" => 0, "DEFENSA" => 1, "MEDIOCAMPISTA" => 2, "DELANTERO" => 3, _ => 4
        };
    }
}
