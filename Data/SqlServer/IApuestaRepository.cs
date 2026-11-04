namespace CasaApuestas.Data.SqlServer
{
    /// <summary>Una apuesta ya guardada en SQL Server, con sus selecciones (id de cuota y cuota guardada).</summary>
    public record ApuestaGuardada(
        long IdApuesta, decimal Importe, decimal CuotaTotal, decimal PosibleGanancia,
        IReadOnlyList<(int IdCuota, decimal Cuota)> Selecciones);

    /// <summary>Salida de sp_RegistrarApuesta. Repetida = 1: la clave ya existia, no se cobro de nuevo.</summary>
    public record ResultadoRegistro(long IdApuesta, decimal CuotaTotal, decimal PosibleGanancia, bool Repetida);

    /// <summary>
    /// Una fila de vw_DetalleApuestas: una SELECCION de una apuesta (la apuesta se repite en cada una de sus selecciones).
    /// Solo las columnas necesarias: se deja fuera a proposito documento y nombre del cliente que trae la vista.
    /// </summary>
    public record FilaApuesta(
        long IdApuesta, DateTime Fecha, decimal Importe, decimal CuotaTotal, decimal PosibleGanancia, string EstadoApuesta,
        long IdDetalle, int IdCuota, decimal Cuota, string EstadoSeleccion, decimal? ValorCashout = null);

    /// <summary>Un movimiento de saldo del cliente (recarga, apuesta, premio, devolucion...).</summary>
    public record MovimientoSaldoFila(long IdMovimiento, DateTime Fecha, string Tipo, decimal Importe, string Concepto, long? IdApuesta);

    /// <summary>sp_CashOut rechazo el cobro con uno de sus errores controlados (50012, 50032, 50033 o 50034).</summary>
    public class CashOutRechazadoException : Exception
    {
        public int Numero { get; }
        public CashOutRechazadoException(int numero, string mensajeSql) : base(mensajeSql) => Numero = numero;
    }

    /// <summary>El procedimiento rechazo la apuesta con uno de sus errores controlados (50020 a 50027).</summary>
    public class ApuestaRechazadaException : Exception
    {
        public int Numero { get; }
        public ApuestaRechazadaException(int numero, string mensajeSql) : base(mensajeSql) => Numero = numero;
    }

    /// <summary>
    /// Acceso a apuestas en SQL Server. El dinero y las apuestas se escriben UNICAMENTE por sp_RegistrarApuesta; la app
    /// no hace INSERT/UPDATE directos en Apuesta, Apuesta_Detalle, Saldo ni Movimiento_Saldo.
    /// </summary>
    public interface IApuestaRepository
    {
        /// <summary>
        /// Busca una apuesta del cliente por su clave de idempotencia (solo lectura). SIEMPRE filtra por id_cliente:
        /// una clave de otro cliente nunca devuelve su apuesta.
        /// </summary>
        Task<ApuestaGuardada?> BuscarPorClaveAsync(int idCliente, string clave, CancellationToken ct = default);

        /// <summary>
        /// Apuestas del cliente con sus selecciones (SOLO LECTURA, desde vw_DetalleApuestas), de la mas reciente a la mas
        /// antigua. Filtra SIEMPRE por el id_cliente recibido, que debe salir de la sesion.
        /// </summary>
        Task<IReadOnlyList<FilaApuesta>> ListarApuestasDeClienteAsync(int idCliente, CancellationToken ct = default);

        /// <summary>
        /// Ejecuta dbo.sp_CashOut: marca la apuesta CASHOUT SOLO si sigue PENDIENTE, guarda valor_cashout y acredita UNA vez (en una
        /// transaccion). Devuelve true si el procedimiento dijo "repetido" (ya estaba en CASHOUT: no se acredito de nuevo).
        /// </summary>
        Task<bool> CashOutAsync(int idCliente, long idApuesta, decimal valor, CancellationToken ct = default);

        /// <summary>Ultimos movimientos de saldo del cliente (SOLO LECTURA, desde Movimiento_Saldo), mas recientes primero.</summary>
        Task<IReadOnlyList<MovimientoSaldoFila>> ListarMovimientosAsync(int idCliente, int maximo, CancellationToken ct = default);

        /// <summary>
        /// Ejecuta dbo.sp_RegistrarApuesta con la lista de selecciones. Lanza ApuestaRechazadaException si el
        /// procedimiento rechaza la apuesta con un error controlado.
        /// </summary>
        Task<ResultadoRegistro> RegistrarAsync(int idCliente, IReadOnlyList<(int IdCuota, decimal Cuota)> selecciones,
            decimal importe, string clave, decimal premioMaximo, CancellationToken ct = default);
    }
}
