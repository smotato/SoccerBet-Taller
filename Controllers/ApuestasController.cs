using CasaApuestas.Models.Apuestas;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Apuestas del cliente. TODO requiere sesion de cliente. El cupon llama a Confirmar por fetch (JSON), con el token
    /// antiforgery en el encabezado RequestVerificationToken. Las respuestas llevan siempre un "codigo" que el cupon entiende.
    /// </summary>
    [Authorize]
    public class ApuestasController : Controller
    {
        private readonly IApuestaService _servicio;
        private readonly ILogger<ApuestasController> _log;

        public ApuestasController(IApuestaService servicio, ILogger<ApuestasController> log)
        {
            _servicio = servicio;
            _log = log;
        }

        // POST /Apuestas/Confirmar   (sin sesion: 401; sin token antiforgery: 400)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirmar([FromBody] ConfirmarApuestaRequest? solicitud, CancellationToken ct)
        {
            // El id del cliente sale SIEMPRE de la sesion (claim), nunca del cuerpo de la peticion.
            if (!int.TryParse(User.FindFirst(AccountController.ClaimIdCliente)?.Value, out var idCliente))
                return Unauthorized(new { exito = false, codigo = "SIN_SESION", mensaje = "Inicia sesión como cliente para apostar." });

            if (solicitud is null)
                return BadRequest(new { exito = false, codigo = "INVALIDA", mensaje = "No pudimos leer tu cupón." });

            try
            {
                var res = await _servicio.ConfirmarAsync(idCliente, solicitud, ct);
                return StatusCode(res.Status, res);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // PostgreSQL/SQL Server caidos o error inesperado. Antes de cobrar no se pudo validar, o el resultado es
                // incierto; como la clave del cupon se conserva, reintentar el MISMO cupon nunca cobra dos veces.
                _log.LogError(ex, "Fallo al confirmar una apuesta del cliente {IdCliente}.", idCliente);
                return StatusCode(503, new
                {
                    exito = false,
                    codigo = "NO_DISPONIBLE",
                    mensaje = "No pudimos procesar tu apuesta en este momento. Si vuelves a confirmar este mismo cupón no se cobrará dos veces."
                });
            }
        }
    }
}
