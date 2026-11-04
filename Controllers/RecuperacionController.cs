using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Recuperacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Recuperar contrasena (RF15). Publico (el usuario no tiene sesion). Todas las respuestas llevan Referrer-Policy: no-referrer
    /// y no se guardan en cache, porque la URL de "Restablecer" trae el token: no debe filtrarse a otros sitios (CDN de la pagina)
    /// ni quedar en el historial de un proxy. El token nunca se escribe en los logs.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class RecuperacionController : Controller
    {
        // Mensaje UNICO: se muestra exista o no el correo.
        public const string MensajeGenerico = "Si el correo existe, te enviamos un enlace para restablecer tu contraseña. Revisa tu bandeja de entrada (y el correo no deseado). El enlace vence en 30 minutos.";

        private readonly IRecuperacionService _servicio;
        private readonly ILogger<RecuperacionController> _log;

        public RecuperacionController(IRecuperacionService servicio, ILogger<RecuperacionController> log)
        {
            _servicio = servicio;
            _log = log;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";
            base.OnActionExecuting(context);
        }

        [HttpGet]
        public IActionResult Olvide() => View(new OlvideViewModel());

        [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("recuperacion-solicitud")]
        public async Task<IActionResult> Olvide(OlvideViewModel model, CancellationToken ct)
        {
            if (!ModelState.IsValid) return View(new OlvideViewModel { Email = model.Email });

            SolicitudRecuperacion r;
            try
            {
                r = await _servicio.SolicitarAsync(model.Email!, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aunque falle SQL Server o el envio: se registra y se responde IGUAL (no se revela nada).
                _log.LogError(ex, "Fallo al procesar una solicitud de recuperacion de contrasena.");
                r = new SolicitudRecuperacion(null);
            }
            return View(new OlvideViewModel { Enviado = true, EnlaceDevelopment = r.EnlaceDevelopment });
        }

        [HttpGet]
        public async Task<IActionResult> Restablecer(string? t, CancellationToken ct)
        {
            if (!await EnlaceValidoSeguroAsync(t, ct)) return View("EnlaceInvalido");
            return View(new RestablecerViewModel { T = t });
        }

        [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("recuperacion-restablecer")]
        public async Task<IActionResult> Restablecer(RestablecerViewModel model, CancellationToken ct)
        {
            // Primero el enlace (si ya no vale, no tiene sentido validar la contrasena) y despues la politica de contrasena.
            if (!await EnlaceValidoSeguroAsync(model.T, ct)) return View("EnlaceInvalido");
            if (!ModelState.IsValid) return View(new RestablecerViewModel { T = model.T });

            bool ok;
            try { ok = await _servicio.RestablecerAsync(model.T, model.Password!, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Fallo al restablecer una contrasena.");
                ModelState.AddModelError("", "No pudimos cambiar tu contraseña ahora. Inténtalo de nuevo en unos minutos.");
                return View(new RestablecerViewModel { T = model.T });
            }
            return ok ? View("Listo") : View("EnlaceInvalido");
        }

        // Si SQL Server falla al validar, el enlace se trata como no valido (nunca se muestra el formulario por error).
        private async Task<bool> EnlaceValidoSeguroAsync(string? t, CancellationToken ct)
        {
            try { return await _servicio.EnlaceValidoAsync(t, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo validar un enlace de recuperacion.");
                return false;
            }
        }
    }
}
