using CasaApuestas.Services;

namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Una recarga que aun no termino (PENDIENTE, o aprobada pero sin acreditar).</summary>
    public class RecargaPendienteItem
    {
        public string Referencia { get; init; } = "";
        public string ImporteTexto { get; init; } = "";
        public string FechaTexto { get; init; } = "";
        public string Estado { get; init; } = "";
        /// <summary>Vacio hasta que el usuario vuelve del checkout; Wompi solo permite consultar por este id.</summary>
        public string IdTransaccion { get; init; } = "";
        public bool PuedeVerificar => IdTransaccion != "";
    }

    public class RecargaIndexViewModel
    {
        public long Minimo { get; init; }
        public long Maximo { get; init; }
        public string MinimoTexto { get; init; } = "";
        public string MaximoTexto { get; init; } = "";
        public IReadOnlyList<RecargaPendienteItem> Pendientes { get; init; } = Array.Empty<RecargaPendienteItem>();
    }

    public class RecargaResultadoViewModel
    {
        public ResultadoRecarga Resultado { get; init; }
        public string Titulo { get; init; } = "";
        public string Mensaje { get; init; } = "";
        public string Icono { get; init; } = "";      // clase de Bootstrap Icons
        public string Tono { get; init; } = "";       // ok | warn | bad
    }
}
