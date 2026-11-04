using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Detalle de partido (PUBLICO: quien no tiene sesion ve las cuotas; apostar si exigira sesion).
    /// NoStore por la misma razon que el home: la cabecera cambia segun haya sesion o no.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class PartidosController : Controller
    {
        private const string MensajeSinDetalle = "No pudimos cargar el partido en este momento. Intenta de nuevo en unos minutos.";

        private readonly IDetallePartidoService _servicio;
        private readonly ILogger<PartidosController> _log;

        public PartidosController(IDetallePartidoService servicio, ILogger<PartidosController> log)
        {
            _servicio = servicio;
            _log = log;
        }

        // GET /Partidos/Detalle/12
        public async Task<IActionResult> Detalle(int id, CancellationToken ct)
        {
            try
            {
                var modelo = await _servicio.ObtenerAsync(id, ct);
                if (modelo is null) return NotFound();
                return View(modelo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo leer el detalle del partido {IdPartido} (PostgreSQL/MySQL).", id);
                return View(new DetallePartidoViewModel { IdPartido = id, Error = MensajeSinDetalle });
            }
        }
    }
}
