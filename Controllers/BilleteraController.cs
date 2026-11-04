using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Billetera del cliente. RF16: retiro simulado. TODO requiere sesion de cliente. NoStore: muestra saldo y retiros,
    /// datos personales que no deben quedar en la cache del navegador. El controlador solo habla con IRetiroService.
    /// </summary>
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class BilleteraController : Controller
    {
        private readonly IRetiroService _retiros;
        private readonly ILogger<BilleteraController> _log;

        public BilleteraController(IRetiroService retiros, ILogger<BilleteraController> log)
        {
            _retiros = retiros;
            _log = log;
        }

        // GET /Billetera/Retirar : formulario + "Mis retiros". Cada vez que se abre se genera una clave (GUID) nueva.
        [HttpGet]
        public async Task<IActionResult> Retirar(CancellationToken ct)
        {
            if (!TryObtenerIdCliente(out var idCliente)) return Forbid();

            try
            {
                return View(await _retiros.ObtenerPaginaAsync(idCliente, mensaje: TempData["Retiro.Ok"] as string, error: TempData["Retiro.Error"] as string, ct: ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo cargar la pagina de retiros del cliente {IdCliente} (SQL Server).", idCliente);
                return View(new RetirarViewModel { Error = "No pudimos cargar tu billetera en este momento. Intenta de nuevo en unos minutos." });
            }
        }

        // POST /Billetera/Retirar (sin sesion: redirige al login; sin token antiforgery: 400).
        // Patron POST-redirect-GET: tras un retiro correcto se redirige, asi un F5 no reenvia el formulario y la cabecera
        // se pinta con el saldo nuevo.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Retirar([FromForm] string? clave, [FromForm] string? importe, [FromForm] string? medio, CancellationToken ct)
        {
            // El id del cliente sale SIEMPRE de la sesion (claim), nunca del formulario.
            if (!TryObtenerIdCliente(out var idCliente)) return Forbid();

            try
            {
                var res = await _retiros.RetirarAsync(idCliente, clave, importe, medio, ct);
                if (res.Exito)
                {
                    TempData["Retiro.Ok"] = res.Mensaje;
                    return RedirectToAction(nameof(Retirar));
                }

                // Error: se vuelve a mostrar el formulario con lo escrito y con la MISMA clave (no se creo ningun retiro).
                return View(await _retiros.ObtenerPaginaAsync(idCliente, clave, importe, medio, error: res.Mensaje, ct: ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SQL Server caido o error inesperado: el resultado puede ser incierto (el retiro pudo quedar registrado), asi que
                // no se vuelve a mostrar el formulario: se pide revisar saldo y "Mis retiros" antes de intentarlo de nuevo.
                _log.LogError(ex, "Fallo al procesar un retiro del cliente {IdCliente}.", idCliente);
                return View(new RetirarViewModel
                {
                    Error = "No pudimos procesar tu retiro en este momento. Revisa tu saldo y tus retiros antes de intentarlo de nuevo."
                });
            }
        }

        private bool TryObtenerIdCliente(out int idCliente) =>
            int.TryParse(User.FindFirst(AccountController.ClaimIdCliente)?.Value, out idCliente);
    }
}
