using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services.Wompi
{
    /// <summary>Transaccion tal como la reporta Wompi (campos de data en GET /v1/transactions/{id}).</summary>
    public class WompiTransaccion
    {
        public string Id { get; init; } = "";
        public string Referencia { get; init; } = "";
        public string Estado { get; init; } = "";          // PENDING, APPROVED, DECLINED, VOIDED, ERROR
        public long MontoEnCentavos { get; init; }
        public string Moneda { get; init; } = "";
        public string MetodoPago { get; init; } = "";      // payment_method_type: CARD, PSE, NEQUI...
        /// <summary>Respuesta completa convertida a tipos que Firestore acepta (para guardarla en el campo payload).</summary>
        public Dictionary<string, object?> Payload { get; init; } = new();
    }

    public interface IWompiClient
    {
        /// <summary>Consulta la transaccion REAL a Wompi. Devuelve null si Wompi dice que no existe.</summary>
        Task<WompiTransaccion?> ObtenerTransaccionAsync(string idTransaccion, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Cliente HTTP de la API de Wompi. La consulta de transacciones solo funciona con la llave PRIVADA
    /// (Authorization: Bearer prv_...), por eso se hace siempre desde el servidor.
    /// La llave va solo en el encabezado: nunca en la URL, ni en logs ni en mensajes de error.
    /// </summary>
    public class WompiClient : IWompiClient
    {
        // El id llega por la URL de retorno (lo controla el usuario): solo se acepta un formato inocuo antes de usarlo en una ruta.
        private static readonly Regex IdValido = new(@"^[A-Za-z0-9_\-]{1,100}$", RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly ILogger<WompiClient> _logger;

        public WompiClient(HttpClient http, IOptions<WompiOptions> opciones, ILogger<WompiClient> logger)
        {
            _http = http;
            _logger = logger;
            var o = opciones.Value;
            _http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            _http.Timeout = TimeSpan.FromSeconds(15);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.PrivateKey);
        }

        public async Task<WompiTransaccion?> ObtenerTransaccionAsync(string idTransaccion, CancellationToken cancellationToken = default)
        {
            if (!IdValido.IsMatch(idTransaccion ?? ""))
                return null;

            using var respuesta = await _http.GetAsync($"transactions/{Uri.EscapeDataString(idTransaccion!)}", cancellationToken);

            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!respuesta.IsSuccessStatusCode)
            {
                // Solo el codigo HTTP: sin URL completa de encabezados ni cuerpo.
                _logger.LogWarning("Wompi respondio HTTP {Codigo} al consultar una transaccion.", (int)respuesta.StatusCode);
                throw new HttpRequestException($"Wompi respondio HTTP {(int)respuesta.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;

            return new WompiTransaccion
            {
                Id = LeerTexto(data, "id"),
                Referencia = LeerTexto(data, "reference"),
                Estado = LeerTexto(data, "status"),
                MontoEnCentavos = data.TryGetProperty("amount_in_cents", out var m) && m.TryGetInt64(out var c) ? c : -1,
                Moneda = LeerTexto(data, "currency"),
                MetodoPago = LeerTexto(data, "payment_method_type"),
                Payload = (Dictionary<string, object?>)ConvertirParaFirestore(data)!
            };
        }

        private static string LeerTexto(JsonElement obj, string campo) =>
            obj.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        // Firestore no acepta JsonElement: se convierte a Dictionary / List / string / long / double / bool / null.
        private static object? ConvertirParaFirestore(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ConvertirParaFirestore(p.Value)),
            JsonValueKind.Array => e.EnumerateArray().Select(ConvertirParaFirestore).ToList(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
