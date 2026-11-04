namespace CasaApuestas.Data.SqlServer
{
    /// <summary>Cuantas apuestas cambiaron de estado en una liquidacion (lo que devuelve sp_LiquidarApuestas).</summary>
    public record ResumenLiquidacion(int Ganadas, int Perdidas, int Anuladas)
    {
        public int Total => Ganadas + Perdidas + Anuladas;
    }

    /// <summary>
    /// Liquidacion de apuestas en SQL Server (RF14). Todo el dinero se mueve DENTRO de los procedimientos almacenados, en una
    /// transaccion; la app no escribe directo en Apuesta, Saldo ni Movimiento_Saldo. Es seguro repetir cualquier llamada: los
    /// procedimientos solo tocan apuestas y selecciones PENDIENTE.
    /// </summary>
    public interface ILiquidacionRepository
    {
        /// <summary>Ids de cuota con alguna seleccion PENDIENTE en una apuesta PENDIENTE (lo que todavia falta liquidar).</summary>
        Task<IReadOnlyList<int>> ListarCuotasPendientesAsync(CancellationToken ct = default);

        /// <summary>sp_ResolverApuestasPartido: marca las selecciones GANADA/PERDIDA y liquida las apuestas que ya se pueden decidir.</summary>
        Task<ResumenLiquidacion> ResolverAsync(IReadOnlyCollection<(int IdCuota, bool Ganadora)> resultados, CancellationToken ct = default);

        /// <summary>sp_AnularApuestasPartido: las selecciones de un partido suspendido salen (cuota 1,00) o se devuelve el importe.</summary>
        Task<ResumenLiquidacion> AnularAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default);
    }
}
