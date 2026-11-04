using CasaApuestas.Data.Postgres;
using CasaApuestas.Models.Catalogo;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Presentacion;

namespace CasaApuestas.Services
{
    public interface IDetallePartidoService
    {
        /// <summary>Devuelve null si el partido no existe.</summary>
        Task<DetallePartidoViewModel?> ObtenerAsync(int idPartido, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "detalle de partido": PostgreSQL entrega partido, jornada, mercados y cuotas; MySQL (via el cache de
    /// equipos) entrega nombres y escudos. Se unen aqui, sin JOIN entre gestores.
    /// En esta fase solo se puede apostar en partidos PROGRAMADO: en EN_VIVO, FINALIZADO o SUSPENDIDO las cuotas se
    /// muestran, pero deshabilitadas.
    /// </summary>
    public class DetallePartidoService : IDetallePartidoService
    {
        // Orden fijo de las selecciones dentro de cada mercado (por codigo estable, no por texto).
        private static readonly string[] OrdenCodigos = { "LOCAL", "EMPATE", "VISITANTE", "MAS_2_5", "MENOS_2_5", "SI", "NO" };

        private readonly IPartidoRepository _partidos;
        private readonly IEquiposCache _catalogo;
        private readonly TimeProvider _reloj;

        public DetallePartidoService(IPartidoRepository partidos, IEquiposCache catalogo, TimeProvider reloj)
        {
            _partidos = partidos;
            _catalogo = catalogo;
            _reloj = reloj;
        }

        public async Task<DetallePartidoViewModel?> ObtenerAsync(int idPartido, CancellationToken ct = default)
        {
            var partido = await _partidos.ObtenerPartidoAsync(idPartido, ct);
            if (partido is null) return null;

            var cuotas = await _partidos.ListarCuotasDePartidoAsync(idPartido, ct);
            var mercados = (await _partidos.ListarMercadosAsync(ct)).Where(m => m.Activo).ToList();
            var calendario = (await _partidos.ListarCalendariosAsync(new[] { partido.IdCalendario }, ct)).FirstOrDefault();
            var equipos = await _catalogo.ObtenerEquiposAsync(new[] { partido.IdEquipoLocal, partido.IdEquipoVisitante }, ct);
            var liga = calendario is null ? null : await _catalogo.ObtenerLigaAsync(calendario.IdLiga, ct);

            var enVivoOFinal = partido.Estado is "EN_VIVO" or "FINALIZADO";
            var local = Mini(equipos, partido.IdEquipoLocal, enVivoOFinal ? partido.GolesLocal ?? 0 : null);
            var visitante = Mini(equipos, partido.IdEquipoVisitante, enVivoOFinal ? partido.GolesVisitante ?? 0 : null);
            var programado = partido.Estado == "PROGRAMADO";

            var vmMercados = mercados
                .Select(m => new MercadoViewModel(m.IdMercado, m.Nombre,
                    cuotas.Where(c => c.IdMercado == m.IdMercado)
                          .OrderBy(c => Array.IndexOf(OrdenCodigos, c.CodigoSeleccion))
                          .Select(c => new SeleccionViewModel(c.IdCuota, EtiquetaCuota.Texto(c, local.Nombre, visitante.Nombre), c.Valor,
                                                              Apostable: programado && c.Disponible))
                          .ToList()))
                .Where(m => m.Selecciones.Count > 0)
                .ToList();

            return new DetallePartidoViewModel
            {
                IdPartido = partido.IdPartido,
                Estado = partido.Estado,
                Hora = FechaColombia.Formatear(partido.FechaHora, FechaColombia.Ahora(_reloj)),
                Minuto = partido.Minuto,
                Competicion = liga is null ? "Liga" : $"{liga.Pais} / {liga.Nombre}",
                Jornada = calendario?.NumeroJornada,
                Local = local,
                Visitante = visitante,
                Mercados = vmMercados
            };
        }

        private static EquipoMiniViewModel Mini(IReadOnlyDictionary<int, Equipo> equipos, int idEquipo, int? goles)
        {
            var nombre = equipos.TryGetValue(idEquipo, out var e) ? e.Nombre : $"Equipo {idEquipo}";
            return new EquipoMiniViewModel(nombre, Escudo.Iniciales(nombre), e?.EscudoUrl, goles);
        }
    }
}
