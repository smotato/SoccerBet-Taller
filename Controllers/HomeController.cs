using System.Diagnostics;
using CasaApuestas.Models;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    // El home es PUBLICO: cualquiera ve partidos y cuotas. [Authorize] se usara solo en las paginas del cliente
    // (apostar, mis apuestas, billetera). NoStore porque la cabecera cambia segun haya sesion o no:
    // asi, tras cerrar sesion, "Atras" no muestra la version con sesion desde la cache.
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class HomeController : Controller
    {
        private const string MensajeSinPartidos = "No pudimos cargar los partidos en este momento. Intenta de nuevo en unos minutos.";

        private readonly ILogger<HomeController> _logger;
        private readonly IPartidoService _partidos;

        public HomeController(ILogger<HomeController> logger, IPartidoService partidos)
        {
            _logger = logger;
            _partidos = partidos;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            try
            {
                return View(await _partidos.ObtenerHomeAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Si PostgreSQL o MySQL no responden el home carga igual (cabecera, menu, login) con un mensaje claro;
                // el detalle tecnico solo va al log.
                _logger.LogError(ex, "No se pudieron leer los partidos del home (PostgreSQL/MySQL).");
                return View(new HomeViewModel { Error = MensajeSinPartidos });
            }
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
