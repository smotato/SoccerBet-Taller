namespace CasaApuestas.Data.SqlServer
{
    /// <summary>Un retiro simulado guardado en SQL Server (tabla dbo.Retiro).</summary>
    public record RetiroFila(long IdRetiro, DateTime Fecha, decimal Importe, string Medio, string Estado);

    /// <summary>Salida de sp_RetirarSaldo. Repetido = 1: la clave ya existia, no se descontó de nuevo.</summary>
    public record ResultadoRetiro(long IdRetiro, bool Repetido);

    /// <summary>El procedimiento rechazo el retiro con uno de sus errores controlados (50021, 50027, 50030, 50031).</summary>
    public class RetiroRechazadoException : Exception
    {
        public int Numero { get; }
        public RetiroRechazadoException(int numero, string mensajeSql) : base(mensajeSql) => Numero = numero;
    }

    /// <summary>
    /// Acceso a retiros en SQL Server. El saldo y los retiros se escriben UNICAMENTE por sp_RetirarSaldo; la app no hace
    /// INSERT/UPDATE directos en Retiro, Saldo ni Movimiento_Saldo. Las lecturas filtran SIEMPRE por id_cliente.
    /// </summary>
    public interface IRetiroRepository
    {
        /// <summary>Retiro del cliente con esa clave de idempotencia (solo lectura), o null. Filtra por id_cliente.</summary>
        Task<RetiroFila?> BuscarPorClaveAsync(int idCliente, string clave, CancellationToken ct = default);

        /// <summary>Ultimos retiros del cliente (solo lectura), del mas reciente al mas antiguo. Filtra por id_cliente.</summary>
        Task<IReadOnlyList<RetiroFila>> ListarAsync(int idCliente, int maximo, CancellationToken ct = default);

        /// <summary>
        /// Ejecuta dbo.sp_RetirarSaldo. Lanza RetiroRechazadoException si el procedimiento rechaza el retiro con un error
        /// controlado. El procedimiento descuenta el saldo y registra el DEBITO en una sola transaccion.
        /// </summary>
        Task<ResultadoRetiro> RegistrarAsync(int idCliente, decimal importe, string medio, string clave, decimal minimo, CancellationToken ct = default);
    }
}
