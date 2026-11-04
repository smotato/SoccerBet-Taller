using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Reglas del futbol y reglas de apuestas (RF8). PUBLICA: no exige sesion. NoStore por la misma razon que el home:
    /// la cabecera cambia segun haya sesion o no. El controlador solo habla con IReglasService.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class ReglasController : Controller
    {
        private const string MensajeSinReglas = "No pudimos cargar las reglas en este momento. Intenta de nuevo en unos minutos.";

        private readonly IReglasService _servicio;
        private readonly ILogger<ReglasController> _log;

        public ReglasController(IReglasService servicio, ILogger<ReglasController> log)
        {
            _servicio = servicio;
            _log = log;
        }

        // GET /Reglas   (el cupon enlaza a /Reglas#apuestas)
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            try
            {
                return View(await _servicio.ObtenerAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // MongoDB caido o mal configurado: el resto del sitio sigue funcionando; aqui solo un mensaje amable.
                _log.LogError(ex, "No se pudieron leer las reglas desde MongoDB.");
                return View(new ReglasViewModel { Error = MensajeSinReglas });
            }
        }
    }
}
