using CasaApuestas.Models.Apuestas;
using CasaApuestas.Models.ViewModels;
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
        private const string MensajeSinApuestas = "No pudimos cargar tus apuestas en este momento. Intenta de nuevo en unos minutos.";

        private readonly IApuestaService _servicio;
        private readonly IMisApuestasService _misApuestas;
        private readonly ICashOutService _cashOut;
        private readonly ILogger<ApuestasController> _log;

        public ApuestasController(IApuestaService servicio, IMisApuestasService misApuestas, ICashOutService cashOut, ILogger<ApuestasController> log)
        {
            _servicio = servicio;
            _misApuestas = misApuestas;
            _cashOut = cashOut;
            _log = log;
        }

        // GET /Apuestas/ValoresCashOut : valor de cash out de las apuestas PENDIENTE del cliente de la SESION (lo refresca cashout.js).
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> ValoresCashOut(CancellationToken ct)
        {
            if (!int.TryParse(User.FindFirst(AccountController.ClaimIdCliente)?.Value, out var idCliente)) return Unauthorized();
            try
            {
                var valores = await _cashOut.CalcularPendientesAsync(idCliente, ct);
                return Json(valores.Select(v => new { idApuesta = v.IdApuesta, disponible = v.Disponible, valor = v.Valor, motivo = v.Motivo }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudieron calcular los valores de cash out del cliente {IdCliente}.", idCliente);
                return StatusCode(503, new { error = "No pudimos actualizar el cash out en este momento." });
            }
        }

        // POST /Apuestas/ConfirmarCashOut   (sin sesion: 401; sin token antiforgery: 400; apuesta ajena: 404)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarCashOut([FromBody] CashOutSolicitud? solicitud, CancellationToken ct)
        {
            // El id del cliente sale SIEMPRE de la sesion (claim), nunca del cuerpo.
            if (!int.TryParse(User.FindFirst(AccountController.ClaimIdCliente)?.Value, out var idCliente))
                return Unauthorized(new { exito = false, codigo = "SIN_SESION", mensaje = "Inicia sesión como cliente." });
            if (solicitud is null || solicitud.IdApuesta <= 0)
                return BadRequest(new { exito = false, codigo = "INVALIDA", mensaje = "No pudimos leer tu solicitud." });

            try
            {
                var res = await _cashOut.ConfirmarAsync(idCliente, solicitud.IdApuesta, solicitud.ValorVisto, ct);
                return StatusCode(res.Status, res);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // PostgreSQL o SQL Server caidos: no se cobro nada que no se pueda reintentar (el cash out es idempotente por estado).
                _log.LogError(ex, "Fallo el cash out de la apuesta {IdApuesta} del cliente {IdCliente}.", solicitud.IdApuesta, idCliente);
                return StatusCode(503, new { exito = false, codigo = "NO_DISPONIBLE", mensaje = "No pudimos procesar el cash out en este momento. Revisa Mis apuestas antes de reintentar." });
            }
        }

        // GET /Apuestas  ->  "Mis apuestas". Requiere sesion de cliente (sin sesion redirige al login).
        // NoStore: son datos personales; tras cerrar sesion, "Atras" no debe mostrarlos desde la cache del navegador.
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            // El id del cliente sale SIEMPRE de la sesion (claim): no existe ningun parametro para pedir las apuestas de otro.
            if (!int.TryParse(User.FindFirst(AccountController.ClaimIdCliente)?.Value, out var idCliente))
                return Forbid();

            try
            {
                return View(await _misApuestas.ObtenerAsync(idCliente, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudieron leer las apuestas del cliente {IdCliente} (SQL Server).", idCliente);
                return View(new MisApuestasViewModel { Error = MensajeSinApuestas });
            }
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

    /// <summary>Cuerpo de POST /Apuestas/ConfirmarCashOut: la apuesta y el valor que el cliente vio (el servidor recalcula).</summary>
    public class CashOutSolicitud
    {
        public long IdApuesta { get; set; }
        public long ValorVisto { get; set; }
    }
