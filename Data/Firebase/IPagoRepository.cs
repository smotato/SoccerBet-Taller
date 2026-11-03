namespace CasaApuestas.Data.Firebase
{
    /// <summary>Un documento de la coleccion "pagos" de Firestore (ver docs/firestore.md).</summary>
    public class PagoRecarga
    {
        public string Referencia { get; init; } = "";
        public long IdCliente { get; init; }
        public string IdTransaccion { get; init; } = "";   // vacio hasta que el usuario vuelve del checkout
        public long Importe { get; init; }                 // pesos COP enteros
        public string Metodo { get; init; } = "";
        public string Estado { get; init; } = "";          // PENDIENTE, APROBADO, RECHAZADO, ERROR
        public bool Acreditado { get; init; }
        public DateTime? AcreditandoDesde { get; init; }   // UTC; hora en que alguien reclamo acreditar (null si nadie)
        public DateTime Fecha { get; init; }               // UTC
    }

    /// <summary>
    /// Repositorio de pagos (Firebase/Firestore). El servicio de recarga solo conoce esta interfaz,
    /// no sabe que detras hay Firestore (regla de arquitectura de CLAUDE.md).
    /// </summary>
    public interface IPagoRepository
    {
        /// <summary>Crea el pago en PENDIENTE con acreditado=false. Falla si la referencia ya existe.</summary>
        Task CrearPendienteAsync(long idCliente, string referencia, long importePesos, CancellationToken ct = default);

        Task<PagoRecarga?> ObtenerAsync(string referencia, CancellationToken ct = default);

        /// <summary>Recargas del cliente que aun no se han acreditado ni rechazado (PENDIENTE, o APROBADO sin acreditar).</summary>
        Task<IReadOnlyList<PagoRecarga>> ListarPendientesAsync(long idCliente, CancellationToken ct = default);

        /// <summary>Guarda lo que Wompi reporta (id, estado, metodo, respuesta completa). Un pago ya APROBADO no retrocede.</summary>
        Task ActualizarDesdeWompiAsync(string referencia, string idTransaccion, string estado, string metodo,
                                       IDictionary<string, object?> payload, CancellationToken ct = default);

        /// <summary>
        /// Reclama el derecho a acreditar, de forma atomica (transaccion de Firestore). Devuelve true solo a UN llamador:
        /// si el pago ya esta acreditado, o alguien lo reclamo hace menos de <paramref name="vencimiento"/>, devuelve false.
        /// Un reclamo mas viejo se considera abandonado (la app pudo caerse) y se puede tomar de nuevo.
        /// </summary>
        Task<bool> ReclamarAcreditacionAsync(string referencia, TimeSpan vencimiento, CancellationToken ct = default);

        /// <summary>acreditado=true y se limpia el reclamo.</summary>
        Task MarcarAcreditadoAsync(string referencia, CancellationToken ct = default);

        /// <summary>Quita el reclamo (por ejemplo, si el SP fallo) para poder reintentar sin esperar el vencimiento.</summary>
        Task LiberarReclamoAsync(string referencia, CancellationToken ct = default);
    }
}
