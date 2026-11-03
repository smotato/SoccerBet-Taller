using System.Globalization;
using System.Security.Claims;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// RF12: recarga de saldo con Wompi (sandbox). Solo clientes con sesion ([Authorize] a nivel de clase).
    /// El controlador no sabe nada de Wompi, Firestore ni SQL Server: solo habla con IRecargaService.
    /// NoStore: la pagina muestra pagos y saldo, no debe quedar en la cache del navegador.
    /// </summary>
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class RecargaController : Controller
    {
        // Colombia no tiene horario de verano: UTC-5 fijo. Evita depender de los nombres de zona del sistema operativo.
        private static readonly TimeSpan OffsetColombia = TimeSpan.FromHours(-5);

        // Un id de transaccion de Wompi solo tiene letras, numeros y guiones (ej. 11004-1730400000-12345).
        private static readonly System.Text.RegularExpressions.Regex IdTransaccionSeguro =
            new(@"^[A-Za-z0-9\-]{1,100}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly IRecargaService _recarga;
        private readonly ILogger<RecargaController> _logger;

        public RecargaController(IRecargaService recarga, ILogger<RecargaController> logger)
        {
            _recarga = recarga;
            _logger = logger;
        }

        // GET /Recarga : formulario de monto + recargas pendientes del cliente.
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            if (!TryObtenerIdCliente(out var idCliente)) return Forbid();

            IReadOnlyList<Data.Firebase.PagoRecarga> pendientes;
            try
            {
                pendientes = await _recarga.ListarPendientesAsync(idCliente, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudieron listar las recargas pendientes del cliente {IdCliente}.", idCliente);
                pendientes = Array.Empty<Data.Firebase.PagoRecarga>();
                ViewBag.Error = "No pudimos consultar tus recargas pendientes en este momento.";
            }

            var (min, max) = _recarga.Limites;
            return View(new RecargaIndexViewModel
            {
                Minimo = (long)min,
                Maximo = (long)max,
                MinimoTexto = Pesos(min),
                MaximoTexto = Pesos(max),
                Pendientes = pendientes.Select(p => new RecargaPendienteItem
                {
                    Referencia = p.Referencia,
                    ImporteTexto = Pesos(p.Importe),
                    FechaTexto = (DateTime.SpecifyKind(p.Fecha, DateTimeKind.Utc) + OffsetColombia)
                                    .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    Estado = p.Estado == "APROBADO" ? "Aprobada, acreditando" : "Esperando pago",
                    IdTransaccion = p.IdTransaccion
                }).ToList()
            });
        }

        // POST /Recarga/Iniciar : valida el monto, registra el pago PENDIENTE y manda al usuario al checkout de Wompi.
        // Antiforgery: sin el token, otro sitio no puede disparar recargas en nombre del usuario.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Iniciar(long monto, CancellationToken ct)
        {
            if (!TryObtenerIdCliente(out var idCliente)) return Forbid();

            try
            {
                // La URL de retorno (publica, via Dev Tunnel) la decide el servicio desde la configuracion.
                var inicio = await _recarga.IniciarAsync(idCliente, monto, ct);
                if (inicio.Checkout is null)
                {
                    TempData["Error"] = inicio.Error;
                    return RedirectToAction(nameof(Index));
                }
                // Esta vista contiene SOLO datos publicos (llave publica, referencia, monto, firma).
                return View("Checkout", inicio.Checkout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo iniciar la recarga del cliente {IdCliente}.", idCliente);
                TempData["Error"] = "No pudimos iniciar la recarga. Intenta de nuevo en unos minutos.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET /Recarga/Puente?id=... : Wompi devuelve al usuario aqui, a la direccion PUBLICA (Dev Tunnel). Este navegador
        // llega al dominio del tunel, donde NO hay cookie de sesion (la cookie es de localhost), por eso es [AllowAnonymous]
        // y solo rebota a la direccion local, donde si hay sesion y [Authorize] aplica a Retorno.
        // Seguridad (evita un redireccionamiento abierto): el destino sale SOLO de la configuracion (Wompi:LocalBaseUrl);
        // de la URL se copia unicamente el id, y solo si contiene letras, numeros y guiones. No se lee Host ni ningun otro parametro.
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Puente(string? id)
        {
            if (id is null || !IdTransaccionSeguro.IsMatch(id))
                return BadRequest("Identificador de transacción no válido.");

            var destino = _recarga.UrlRetornoLocal(id);
            if (destino is null)
            {
                _logger.LogWarning("Puente: falta o no es valida la configuracion Wompi:LocalBaseUrl.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Retorno no configurado (Wompi:LocalBaseUrl).");
            }
            return Redirect(destino);
        }

        // GET /Recarga/Retorno?id=... : el usuario vuelve de Wompi (via el puente). El "id" de la URL NO prueba nada: solo dice que
        // transaccion consultar. El estado real se pregunta a Wompi. Es seguro abrirla muchas veces (idempotente).
        [HttpGet]
        public async Task<IActionResult> Retorno(string? id, CancellationToken ct)
        {
            if (!TryObtenerIdCliente(out var idCliente)) return Forbid();
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));

            try
            {
                var confirmacion = await _recarga.ConfirmarAsync(idCliente, id, ct);
                return View("Resultado", ArmarResultado(confirmacion));
            }
            catch (Exception ex)
            {
                // El mensaje de la excepcion no se muestra al usuario; en el log no hay llaves (ver WompiClient).
                _logger.LogError(ex, "Error confirmando una recarga del cliente {IdCliente}.", idCliente);
                return View("Resultado", new RecargaResultadoViewModel
                {
                    Titulo = "No pudimos confirmar tu recarga",
                    Mensaje = "Hubo un problema al consultar el pago. Tu dinero no se pierde: vuelve a Recargas y pulsa \"Verificar\" en unos minutos.",
                    Icono = "bi-exclamation-triangle-fill",
                    Tono = "warn"
                });
            }
        }

        // POST /Recarga/Verificar : boton de la lista de pendientes. Reutiliza exactamente el mismo flujo que el retorno
        // (Post-Redirect-Get, asi refrescar la pagina no reenvia el formulario).
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Verificar(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
            return RedirectToAction(nameof(Retorno), new { id });
        }

        // --------------------------------------------------------------------------------------------
        private bool TryObtenerIdCliente(out int idCliente) =>
            int.TryParse(User.FindFirstValue(AccountController.ClaimIdCliente), out idCliente);

        private static string Pesos(decimal valor) =>
            "$ " + valor.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

        private static RecargaResultadoViewModel ArmarResultado(ConfirmacionRecarga c)
        {
            var monto = Pesos(c.Importe);
            return c.Resultado switch
            {
                ResultadoRecarga.Acreditada => Resultado(c, "Recarga exitosa",
                    $"Se acreditaron {monto} a tu saldo.", "bi-check-circle-fill", "ok"),
                ResultadoRecarga.YaAcreditada => Resultado(c, "Recarga ya acreditada",
                    $"Esta recarga de {monto} ya estaba acreditada en tu saldo. No se sumó de nuevo.", "bi-check-circle-fill", "ok"),
                ResultadoRecarga.EnProceso => Resultado(c, "Acreditando tu recarga",
                    "Estamos acreditando este pago. Espera unos segundos y actualiza la página.", "bi-hourglass-split", "warn"),
                ResultadoRecarga.Pendiente => Resultado(c, "Pago pendiente",
                    "Wompi aún no confirma el pago. Cuando se confirme, pulsa \"Verificar\" en Recargas para acreditarlo.", "bi-hourglass-split", "warn"),
                ResultadoRecarga.Rechazada => Resultado(c, "Pago rechazado",
                    "Wompi rechazó el pago, así que no se cobró ni se modificó tu saldo. Puedes intentarlo de nuevo.", "bi-x-circle-fill", "bad"),
                ResultadoRecarga.ErrorPasarela => Resultado(c, "Error en el pago",
                    "Ocurrió un error al procesar el pago en Wompi. Tu saldo no cambió. Intenta de nuevo.", "bi-x-circle-fill", "bad"),
                ResultadoRecarga.NoCoincide => Resultado(c, "No pudimos validar el pago",
                    "El monto del pago no coincide con el registrado, por seguridad no se acreditó. Si crees que es un error, contáctanos.", "bi-shield-exclamation", "bad"),
                _ => Resultado(c, "Pago no encontrado",
                    "No encontramos esta transacción en tu cuenta.", "bi-question-circle-fill", "warn")
            };
        }

        private static RecargaResultadoViewModel Resultado(ConfirmacionRecarga c, string titulo, string mensaje, string icono, string tono) =>
            new() { Resultado = c.Resultado, Titulo = titulo, Mensaje = mensaje, Icono = icono, Tono = tono };
    }
}
