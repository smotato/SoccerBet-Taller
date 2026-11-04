using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>Tabla de posiciones (publica, sin [Authorize]). GET /Posiciones</summary>
    [AllowAnonymous]
    public class PosicionesController : Controller
    {
        private const string MensajeSinTabla = "No pudimos cargar la tabla de posiciones en este momento. Intenta de nuevo en unos minutos.";

        private readonly IPosicionesService _servicio;
        private readonly ILogger<PosicionesController> _log;

        public PosicionesController(IPosicionesService servicio, ILogger<PosicionesController> log)
        {
            _servicio = servicio;
            _log = log;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            try
            {
                return View(await _servicio.ObtenerTablaAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo calcular la tabla de posiciones (PostgreSQL/MySQL).");
                return View(new PosicionesViewModel { Error = MensajeSinTabla });
            }
        }
    }
}
