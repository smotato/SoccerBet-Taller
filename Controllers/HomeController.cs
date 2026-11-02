using System.Diagnostics;
using CasaApuestas.Models;
using CasaApuestas.Models.ViewModels;
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
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
