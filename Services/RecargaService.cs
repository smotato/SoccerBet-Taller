using CasaApuestas.Data.Firebase;
using CasaApuestas.Data.SqlServer;
using CasaApuestas.Services.Wompi;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services
{
    public enum ResultadoRecarga
    {
        Acreditada,        // se sumo al saldo en esta llamada
        YaAcreditada,      // ya se habia sumado antes: no se vuelve a sumar
        EnProceso,         // otra peticion la esta acreditando en este momento
        Pendiente,         // Wompi aun no tiene un resultado final
        Rechazada,         // DECLINED o VOIDED
        ErrorPasarela,     // ERROR en Wompi
        NoEncontrada,      // la transaccion o la referencia no existe, o no es de este cliente
        NoCoincide         // monto o moneda de Wompi no coinciden con lo registrado: no se acredita
    }

    public record ConfirmacionRecarga(ResultadoRecarga Resultado, long Importe = 0, string Referencia = "");

    public record InicioRecarga(CheckoutData? Checkout, string? Error);

    public interface IRecargaService
    {
        (decimal Minimo, decimal Maximo) Limites { get; }
        Task<InicioRecarga> IniciarAsync(int idCliente, long montoPesos, string redirectUrl, CancellationToken ct = default);
        Task<ConfirmacionRecarga> ConfirmarAsync(int idCliente, string idTransaccion, CancellationToken ct = default);
        Task<IReadOnlyList<PagoRecarga>> ListarPendientesAsync(int idCliente, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso RF12: recarga de saldo. Es una saga entre tres sistemas sin transaccion comun:
    ///   Firestore (registro del pago) -> Wompi (estado real) -> SQL Server (saldo).
    /// Reglas que la hacen segura:
    ///  1. Nunca se confia en la redireccion: el estado se consulta a Wompi con la llave privada.
    ///  2. Se valida que referencia, cliente, monto y moneda coincidan con lo que SoccerBet registro al iniciar.
    ///  3. Solo APPROVED y acreditado=false acreditan; antes se RECLAMA el pago en Firestore (transaccion), y el
    ///     procedimiento de SQL Server es idempotente por concepto: son dos barreras contra el doble credito.
    /// </summary>
    public class RecargaService : IRecargaService
    {
        private readonly IPagoRepository _pagos;
        private readonly IWompiClient _wompi;
        private readonly IWompiCheckoutBuilder _checkout;
        private readonly IAcreditacionRepository _acreditacion;
        private readonly WompiOptions _opciones;
        private readonly ILogger<RecargaService> _logger;

        public RecargaService(IPagoRepository pagos, IWompiClient wompi, IWompiCheckoutBuilder checkout,
                              IAcreditacionRepository acreditacion, IOptions<WompiOptions> opciones,
                              ILogger<RecargaService> logger)
        {
            _pagos = pagos;
            _wompi = wompi;
            _checkout = checkout;
            _acreditacion = acreditacion;
            _opciones = opciones.Value;
            _logger = logger;
        }

        public (decimal Minimo, decimal Maximo) Limites => (_opciones.MontoMinimo, _opciones.MontoMaximo);

        public async Task<InicioRecarga> IniciarAsync(int idCliente, long montoPesos, string redirectUrl, CancellationToken ct = default)
        {
            // Validacion en el servidor (el formulario tambien valida, pero el navegador no es de fiar).
            if (montoPesos < _opciones.MontoMinimo || montoPesos > _opciones.MontoMaximo)
                return new InicioRecarga(null,
                    string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"El monto debe estar entre $ {_opciones.MontoMinimo:N0} y $ {_opciones.MontoMaximo:N0}.").Replace(',', '.'));

            var datos = _checkout.Crear(idCliente, montoPesos, redirectUrl);

            // Primero se registra el pago PENDIENTE y solo despues se manda al usuario a Wompi:
            // si Firestore falla, el usuario no llega a pagar algo que no podriamos rastrear.
            await _pagos.CrearPendienteAsync(idCliente, datos.Referencia, montoPesos, ct);

            return new InicioRecarga(datos, null);
        }

        public async Task<ConfirmacionRecarga> ConfirmarAsync(int idCliente, string idTransaccion, CancellationToken ct = default)
        {
            // 1. Estado REAL en Wompi (llave privada, servidor a servidor).
            var tx = await _wompi.ObtenerTransaccionAsync(idTransaccion, ct);
            if (tx is null) return new ConfirmacionRecarga(ResultadoRecarga.NoEncontrada);

            // 2. El pago debe existir en Firestore y ser de ESTE cliente. Si no, se responde "no encontrada"
            //    (sin revelar que existe) para que nadie pueda sondear pagos ajenos.
            var pago = await _pagos.ObtenerAsync(tx.Referencia, ct);
            if (pago is null || pago.IdCliente != idCliente)
                return new ConfirmacionRecarga(ResultadoRecarga.NoEncontrada);

            // 3. Monto y moneda deben coincidir con lo que registramos al iniciar (en centavos).
            if (tx.MontoEnCentavos != pago.Importe * 100 || tx.Moneda != _opciones.Moneda)
            {
                _logger.LogWarning("Pago {Referencia}: el monto o la moneda de Wompi no coinciden con lo registrado.", pago.Referencia);
                return new ConfirmacionRecarga(ResultadoRecarga.NoCoincide, pago.Importe, pago.Referencia);
            }

            // 4. Se guarda lo que dice Wompi (estado mapeado, id de transaccion, metodo y respuesta completa).
            await _pagos.ActualizarDesdeWompiAsync(pago.Referencia, tx.Id, MapearEstado(tx.Estado), tx.MetodoPago, tx.Payload, ct);

            switch (tx.Estado)
            {
                case "APPROVED":
                    return await AcreditarAsync(pago, ct);
                case "PENDING":
                    return new ConfirmacionRecarga(ResultadoRecarga.Pendiente, pago.Importe, pago.Referencia);
                case "ERROR":
                    return new ConfirmacionRecarga(ResultadoRecarga.ErrorPasarela, pago.Importe, pago.Referencia);
                default: // DECLINED, VOIDED
                    return new ConfirmacionRecarga(ResultadoRecarga.Rechazada, pago.Importe, pago.Referencia);
            }
        }

        public Task<IReadOnlyList<PagoRecarga>> ListarPendientesAsync(int idCliente, CancellationToken ct = default) =>
            _pagos.ListarPendientesAsync(idCliente, ct);

        // ---------------------------------------------------------------------------------------------
        private async Task<ConfirmacionRecarga> AcreditarAsync(PagoRecarga pago, CancellationToken ct)
        {
            // Barrera 0 (barata): ya acreditado antes -> no se hace nada.
            if (pago.Acreditado)
                return new ConfirmacionRecarga(ResultadoRecarga.YaAcreditada, pago.Importe, pago.Referencia);

            // Barrera 1: reclamo atomico en Firestore. Solo UNA peticion lo obtiene. Un reclamo con mas de
            // N minutos (por defecto 2) se considera abandonado -la app pudo caerse a mitad- y se puede retomar.
            var vencimiento = TimeSpan.FromMinutes(_opciones.ReclamoVencimientoMinutos);
            if (!await _pagos.ReclamarAcreditacionAsync(pago.Referencia, vencimiento, ct))
            {
                var actual = await _pagos.ObtenerAsync(pago.Referencia, ct);
                return new ConfirmacionRecarga(
                    actual?.Acreditado == true ? ResultadoRecarga.YaAcreditada : ResultadoRecarga.EnProceso,
                    pago.Importe, pago.Referencia);
            }

            bool yaAcreditadoEnSql;
            try
            {
                // Barrera 2: el SP es idempotente por concepto (unico por pago). Si el reclamo se retomo despues de una
                // caida justo tras acreditar, el SP detecta el concepto repetido y responde "ya acreditado" sin sumar.
                yaAcreditadoEnSql = await _acreditacion.AcreditarAsync(
                    (int)pago.IdCliente, pago.Importe, $"Recarga Wompi {pago.Referencia}", ct);
            }
            catch
            {
                // El SP no acredito: se libera el reclamo para poder reintentar enseguida (con "Verificar").
                // Si esto tambien falla, el reclamo simplemente vence solo.
                try { await _pagos.LiberarReclamoAsync(pago.Referencia, CancellationToken.None); } catch { /* vence solo */ }
                throw;
            }

            // Si esto falla (Firestore caido) el saldo YA esta acreditado; el pago queda con reclamo y, al reintentar,
            // el SP responde "ya acreditado" y aqui se marca acreditado=true. Nunca se suma dos veces.
            await _pagos.MarcarAcreditadoAsync(pago.Referencia, CancellationToken.None);

            return new ConfirmacionRecarga(
                yaAcreditadoEnSql ? ResultadoRecarga.YaAcreditada : ResultadoRecarga.Acreditada,
                pago.Importe, pago.Referencia);
        }

        // Estados de Wompi -> estados de nuestra coleccion pagos (docs/firestore.md).
        private static string MapearEstado(string estadoWompi) => estadoWompi switch
        {
            "APPROVED" => "APROBADO",
            "DECLINED" or "VOIDED" => "RECHAZADO",
            "ERROR" => "ERROR",
            _ => "PENDIENTE"
        };
    }
}
