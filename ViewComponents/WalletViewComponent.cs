using System.Security.Claims;
using CasaApuestas.Controllers;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using CasaApuestas.Services.Presentacion;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.ViewComponents
{
    /// <summary>
    /// Zona derecha de la cabecera. Se invoca desde _Layout (@await Component.InvokeAsync("Wallet")),
    /// asi ningun controlador tiene que consultar el saldo para pintar la cabecera.
    /// Visitante: botones "Iniciar sesion" y "Registrarse". Cliente: saldo real, Recargar, avatar y salir.
    /// </summary>
    public class WalletViewComponent : ViewComponent
    {
        private readonly ISaldoService _saldoService;
        private readonly ILogger<WalletViewComponent> _logger;

        public WalletViewComponent(ISaldoService saldoService, ILogger<WalletViewComponent> logger)
        {
            _saldoService = saldoService;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var usuario = UserClaimsPrincipal;

            // Cliente = sesion iniciada con claim id_cliente valido (lo emite AccountController.ValidarLogin).
            if (usuario.Identity?.IsAuthenticated != true ||
                !int.TryParse(usuario.FindFirstValue(AccountController.ClaimIdCliente), out var idCliente))
            {
                return View(new WalletViewModel { EsCliente = false });
            }

            var nombre = usuario.Identity.Name ?? "";
            var modelo = new WalletViewModel
            {
                EsCliente = true,
                Nombre = nombre,
                Iniciales = ObtenerIniciales(nombre)
            };

            try
            {
                modelo.SaldoTexto = Pesos.Formatear(await _saldoService.ObtenerSaldoAsync(idCliente));
            }
            catch (Exception ex)
            {
                // Un fallo al leer el saldo no debe tumbar todas las paginas: se registra y se muestra "—".
                _logger.LogError(ex, "No se pudo consultar el saldo del cliente {IdCliente}.", idCliente);
                modelo.SaldoTexto = "—";
            }

            return View(modelo);
        }

        // Primera letra del primer y del ultimo nombre/apellido: "Juan Carlos Perez Gomez" -> "JG".
        private static string ObtenerIniciales(string nombre)
        {
            var partes = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0) return "?";
            var iniciales = partes.Length == 1 ? partes[0][..1] : partes[0][..1] + partes[^1][..1];
            return iniciales.ToUpperInvariant();
        }
    }
}
