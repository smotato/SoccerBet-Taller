using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services.Wompi
{
    /// <summary>Datos que el navegador necesita para abrir el Web Checkout. No incluye ningun secreto.</summary>
    public record CheckoutData(
        string CheckoutUrl,
        string PublicKey,
        string Moneda,
        long MontoEnCentavos,
        string Referencia,
        string FirmaIntegridad,
        string RedirectUrl);

    public interface IWompiCheckoutBuilder
    {
        CheckoutData Crear(int idCliente, decimal montoPesos, string redirectUrl);
    }

    /// <summary>
    /// Arma los parametros del Web Checkout de Wompi (todo en el SERVIDOR).
    /// Documentacion oficial (docs.wompi.co/docs/colombia/widget-checkout-web):
    ///   firma = SHA-256 (hex, minusculas) de  referencia + monto_en_centavos + moneda + secreto_de_integridad,
    ///   concatenados sin separadores (no usamos expiration-time, que agregaria la fecha antes del secreto).
    /// </summary>
    public class WompiCheckoutBuilder : IWompiCheckoutBuilder
    {
        private readonly WompiOptions _opciones;

        public WompiCheckoutBuilder(IOptions<WompiOptions> opciones)
        {
            _opciones = opciones.Value;
        }

        public CheckoutData Crear(int idCliente, decimal montoPesos, string redirectUrl)
        {
            // Se recargan pesos enteros: Wompi trabaja en centavos enteros (20.000 COP = 2000000).
            if (montoPesos != decimal.Truncate(montoPesos))
                throw new ArgumentException("El monto debe ser en pesos enteros.", nameof(montoPesos));

            long centavos = checked((long)montoPesos * 100L);

            // Referencia UNICA por intento de recarga (Wompi no permite reutilizarla). El GUID la hace impredecible
            // y el idCliente facilita auditarla. Se genera aqui, nunca viene del navegador.
            string referencia = $"SB-{idCliente}-{Guid.NewGuid():N}";

            string firma = CalcularFirma(referencia, centavos, _opciones.Moneda, _opciones.IntegritySecret);

            return new CheckoutData(_opciones.CheckoutUrl, _opciones.PublicKey, _opciones.Moneda,
                                    centavos, referencia, firma, redirectUrl);
        }

        // Expuesto como estatico para poder probar la formula con los ejemplos de la documentacion.
        public static string CalcularFirma(string referencia, long centavos, string moneda, string secretoIntegridad)
        {
            string cadena = $"{referencia}{centavos}{moneda}{secretoIntegridad}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(cadena));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
