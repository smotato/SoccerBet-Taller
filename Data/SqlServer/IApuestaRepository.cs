namespace CasaApuestas.Data.SqlServer
{
    /// <summary>Una apuesta ya guardada en SQL Server, con sus selecciones (id de cuota y cuota guardada).</summary>
    public record ApuestaGuardada(
        long IdApuesta, decimal Importe, decimal CuotaTotal, decimal PosibleGanancia,
        IReadOnlyList<(int IdCuota, decimal Cuota)> Selecciones);

    /// <summary>Salida de sp_RegistrarApuesta. Repetida = 1: la clave ya existia, no se cobro de nuevo.</summary>
    public record ResultadoRegistro(long IdApuesta, decimal CuotaTotal, decimal PosibleGanancia, bool Repetida);

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
        /// Ejecuta dbo.sp_RegistrarApuesta con la lista de selecciones. Lanza ApuestaRechazadaException si el
        /// procedimiento rechaza la apuesta con un error controlado.
        /// </summary>
        Task<ResultadoRegistro> RegistrarAsync(int idCliente, IReadOnlyList<(int IdCuota, decimal Cuota)> selecciones,
            decimal importe, string clave, decimal premioMaximo, CancellationToken ct = default);
    }
}
