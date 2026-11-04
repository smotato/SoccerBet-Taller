using CasaApuestas.Data.SqlServer;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Presentacion;

namespace CasaApuestas.Services
{
    public interface IMisApuestasService
    {
        Task<MisApuestasViewModel> ObtenerAsync(int idCliente, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "Mis apuestas". Las apuestas, sus estados, los importes y los movimientos viven en SQL Server
    /// (solo lectura, siempre filtrados por el id_cliente de la SESION). Los nombres de partidos, mercados y selecciones
    /// salen de PostgreSQL (por id_cuota, en una sola consulta IN) y de MySQL (nombres de equipos, con cache) y se unen aqui.
    /// Si PostgreSQL no responde, la pagina se muestra igual con lo que guarda SQL Server.
    /// </summary>
    public class MisApuestasService : IMisApuestasService
    {
        private const int MaxMovimientos = 100;

        private readonly IApuestaRepository _apuestas;
        private readonly IContextoCuotasLoader _contexto;
        private readonly TimeProvider _reloj;
        private readonly ILogger<MisApuestasService> _log;

        public MisApuestasService(IApuestaRepository apuestas, IContextoCuotasLoader contexto, TimeProvider reloj, ILogger<MisApuestasService> log)
        {
            _apuestas = apuestas;
            _contexto = contexto;
            _reloj = reloj;
            _log = log;
        }

        public async Task<MisApuestasViewModel> ObtenerAsync(int idCliente, CancellationToken ct = default)
        {
            // 1. SQL Server (obligatorio): si falla, la excepcion sube y el controlador muestra un mensaje.
            var filas = await _apuestas.ListarApuestasDeClienteAsync(idCliente, ct);
            var movimientos = await _apuestas.ListarMovimientosAsync(idCliente, MaxMovimientos, ct);

            // 2. PostgreSQL + MySQL (best-effort): solo para los nombres. Una consulta por tabla, no una por apuesta.
            var contexto = ContextoCuotas.Vacio;
            var detalleNoDisponible = false;
            if (filas.Count > 0)
            {
                try
                {
                    contexto = await _contexto.CargarAsync(filas.Select(f => f.IdCuota).Distinct().ToList(), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "No se pudo leer el detalle de los partidos de Mis apuestas (PostgreSQL).");
                    detalleNoDisponible = true;
                }
            }

            var ahora = FechaColombia.Ahora(_reloj);

            // La vista trae una fila por seleccion: se agrupan por apuesta conservando el orden (mas reciente primero).
            var apuestas = filas
                .GroupBy(f => f.IdApuesta)
                .Select(g =>
                {
                    var primera = g.First();
                    var selecciones = g.OrderBy(f => f.IdDetalle).Select(f =>
                    {
                        if (contexto.Cuotas.TryGetValue(f.IdCuota, out var c))
                        {
                            var t = contexto.Textos(c);
                            return new SeleccionApuestaViewModel(f.IdCuota, t.Partido, t.Mercado, t.Seleccion, f.Cuota, f.EstadoSeleccion, true);
                        }
                        return new SeleccionApuestaViewModel(f.IdCuota, "", "", $"Selección (cuota #{f.IdCuota})", f.Cuota, f.EstadoSeleccion, false);
                    }).ToList();

                    return new ApuestaViewModel(primera.IdApuesta, FechaColombia.Formatear(primera.Fecha, ahora), primera.EstadoApuesta,
                        primera.Importe, primera.CuotaTotal, primera.PosibleGanancia, selecciones);
                }).ToList();

            return new MisApuestasViewModel
            {
                Apuestas = apuestas,
                DetalleNoDisponible = detalleNoDisponible,
                Movimientos = movimientos.Select(m =>
                    new MovimientoViewModel(FechaColombia.Formatear(m.Fecha, ahora), m.Concepto, m.Tipo, m.Importe, m.IdApuesta)).ToList()
            };
        }
    }
}
