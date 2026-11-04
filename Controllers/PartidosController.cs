using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CasaApuestas.Controllers
{
    /// <summary>
    /// Detalle de partido (PUBLICO: quien no tiene sesion ve las cuotas; apostar si exigira sesion).
    /// NoStore por la misma razon que el home: la cabecera cambia segun haya sesion o no.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class PartidosController : Controller
    {
        private const string MensajeSinDetalle = "No pudimos cargar el partido en este momento. Intenta de nuevo en unos minutos.";

        // Maximo de partidos que se pueden consultar en una sola peticion de estado.
        private const int MaxIdsEstado = 30;

        private readonly IDetallePartidoService _servicio;
        private readonly IEstadoVivoService _estadoVivo;
        private readonly ILogger<PartidosController> _log;

        public PartidosController(IDetallePartidoService servicio, IEstadoVivoService estadoVivo, ILogger<PartidosController> log)
        {
            _servicio = servicio;
            _estadoVivo = estadoVivo;
            _log = log;
        }

        // GET /Partidos/Estado?ids=11,12   (JSON; publico; solo lectura)
        // Lo consulta vivo.js cada ~5 s en el home y en el detalle para refrescar minuto, marcador y cuotas sin recargar la pagina.
        [HttpGet]
        public async Task<IActionResult> Estado([FromQuery] string? ids, CancellationToken ct)
        {
            // Entrada del navegador: solo enteros positivos, sin repetidos y con tope; cualquier otra cosa se ignora.
            var lista = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var n) && n > 0 ? n : (int?)null)
                .Where(n => n.HasValue).Select(n => n!.Value).Distinct().Take(MaxIdsEstado).ToList();
            if (lista.Count == 0) return BadRequest(new { error = "Indica al menos un id de partido en ?ids=" });

            try
            {
                return Json(await _estadoVivo.ObtenerAsync(lista, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo leer el estado en vivo de los partidos {Ids}.", string.Join(",", lista));
                return StatusCode(503, new { error = "No pudimos actualizar los partidos en este momento." });
            }
        }

        // GET /Partidos/Detalle/12
        public async Task<IActionResult> Detalle(int id, CancellationToken ct)
        {
            try
            {
                var modelo = await _servicio.ObtenerAsync(id, ct);
                if (modelo is null) return NotFound();
                return View(modelo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo leer el detalle del partido {IdPartido} (PostgreSQL/MySQL).", id);
                return View(new DetallePartidoViewModel { IdPartido = id, Error = MensajeSinDetalle });
            }
        }
    }
}
