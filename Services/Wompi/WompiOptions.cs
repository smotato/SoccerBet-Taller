namespace CasaApuestas.Services.Wompi
{
    /// <summary>
    /// Configuracion de Wompi (seccion "Wompi"). Las llaves (PublicKey, PrivateKey, EventsSecret, IntegritySecret)
    /// vienen de user-secrets; el resto (URLs y limites) de appsettings.json. Esta clase NUNCA se imprime ni se registra en logs.
    /// </summary>
    public class WompiOptions
    {
        public const string Seccion = "Wompi";

        // Llaves (user-secrets).
        public string PublicKey { get; set; } = "";
        public string PrivateKey { get; set; } = "";
        public string EventsSecret { get; set; } = "";     // se usara en la segunda etapa (webhook)
        public string IntegritySecret { get; set; } = "";

        // No secretos (appsettings.json). Documentacion oficial: sandbox = https://sandbox.wompi.co/v1,
        // Web Checkout = https://checkout.wompi.co/p/
        public string BaseUrl { get; set; } = "https://sandbox.wompi.co/v1";
        public string CheckoutUrl { get; set; } = "https://checkout.wompi.co/p/";
        public string Moneda { get; set; } = "COP";

        // Limites de una recarga, en pesos.
        public decimal MontoMinimo { get; set; } = 10_000m;
        public decimal MontoMaximo { get; set; } = 1_000_000m;

        // Minutos tras los cuales un "reclamo" de acreditacion sin terminar se considera abandonado y puede reintentarse.
        public int ReclamoVencimientoMinutos { get; set; } = 2;

        // ---- Retorno del checkout en desarrollo (user-secrets) --------------------------------------------------
        // El firewall de Wompi (CloudFront) responde 403 si redirect-url apunta a localhost. Por eso Wompi se redirige a una
        // URL PUBLICA (un Dev Tunnel) y esa URL "rebota" al sitio local:
        //   Wompi -> {PublicBaseUrl}/Recarga/Puente -> {LocalBaseUrl}/Recarga/Retorno?id=...
        // Las dos direcciones salen SOLO de la configuracion, nunca de la peticion (evita un redireccionamiento abierto).
        public string PublicBaseUrl { get; set; } = "";   // ej. https://abc123-7080.use2.devtunnels.ms
        public string LocalBaseUrl { get; set; } = "";    // ej. https://localhost:7080

        /// <summary>URL absoluta que se envia a Wompi como redirect-url, o null si PublicBaseUrl falta o no es una URL https valida.</summary>
        public string? UrlRedireccionWompi() =>
            BaseValida(PublicBaseUrl, soloHttps: true) is { } baseUrl ? baseUrl + "/Recarga/Puente" : null;

        /// <summary>Destino local al que rebota el puente, o null si LocalBaseUrl falta o no es valida. El id debe venir ya validado.</summary>
        public string? UrlRetornoLocal(string idTransaccion) =>
            BaseValida(LocalBaseUrl, soloHttps: false) is { } baseUrl
                ? baseUrl + "/Recarga/Retorno?id=" + Uri.EscapeDataString(idTransaccion) : null;

        // Acepta solo "esquema://host[:puerto]" (sin ruta, consulta, fragmento ni usuario) y devuelve la base sin barra final.
        private static string? BaseValida(string? valor, bool soloHttps)
        {
            if (string.IsNullOrWhiteSpace(valor) || !Uri.TryCreate(valor.Trim(), UriKind.Absolute, out var uri)) return null;
            var esquemaOk = uri.Scheme == Uri.UriSchemeHttps || (!soloHttps && uri.Scheme == Uri.UriSchemeHttp);
            if (!esquemaOk || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "") return null;
            return uri.GetLeftPart(UriPartial.Authority);
        }

        /// <summary>
        /// Falla al arrancar si falta una llave o si no es de PRUEBAS. El proyecto es solo sandbox: asi es imposible
        /// cobrar dinero real por error. Los mensajes dicen QUE falta, nunca el valor.
        /// </summary>
        public void ValidarSandbox()
        {
            Exigir(PublicKey, "PublicKey", "pub_test_");
            Exigir(PrivateKey, "PrivateKey", "prv_test_");
            Exigir(IntegritySecret, "IntegritySecret", "test_integrity_");
            Exigir(EventsSecret, "EventsSecret", "test_events_");
        }

        private static void Exigir(string valor, string nombre, string prefijo)
        {
            if (string.IsNullOrWhiteSpace(valor))
                throw new InvalidOperationException($"Falta la configuracion Wompi:{nombre} (configurala con user-secrets).");
            if (!valor.StartsWith(prefijo, StringComparison.Ordinal))
                throw new InvalidOperationException($"Wompi:{nombre} no parece una llave de pruebas (debe empezar por '{prefijo}').");
        }
    }
}
