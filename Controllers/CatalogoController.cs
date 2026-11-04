using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Catalogo publico (equipos y plantillas). Sin [Authorize]: cualquier visitante puede verlo.
    /// El controlador solo habla con ICatalogoService; no sabe que los datos viven en MySQL.
    /// </summary>
    [AllowAnonymous]
    public class CatalogoController : Controller
    {
        private const string MensajeSinCatalogo = "No pudimos cargar el catálogo en este momento. Intenta de nuevo en unos minutos.";

        private readonly ICatalogoService _servicio;
        private readonly ILogger<CatalogoController> _log;

        public CatalogoController(ICatalogoService servicio, ILogger<CatalogoController> log)
        {
            _servicio = servicio;
            _log = log;
        }

        // GET /Catalogo/Equipos
        public async Task<IActionResult> Equipos(CancellationToken ct)
        {
            try
            {
                return View(await _servicio.ListarEquiposAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El detalle tecnico va al log (sin cadena de conexion); al usuario solo un mensaje claro.
                _log.LogError(ex, "No se pudo leer la lista de equipos desde MySQL.");
                return View(new EquiposListaViewModel { Error = MensajeSinCatalogo });
            }
        }

        // GET /Catalogo/Equipo/5
        public async Task<IActionResult> Equipo(int id, CancellationToken ct)
        {
            try
            {
                var modelo = await _servicio.ObtenerDetalleEquipoAsync(id, ct);
                if (modelo is null) return NotFound();
                return View(modelo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo leer el equipo {IdEquipo} desde MySQL.", id);
                return View(new EquipoDetalleViewModel { IdEquipo = id, Error = MensajeSinCatalogo });
            }
        }
    }
}
