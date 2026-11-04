using CasaApuestas.Data.Postgres;
using CasaApuestas.Data.SqlServer;

namespace CasaApuestas.Services.Simulador
{
    /// <summary>
    /// Liquidacion automatica (RF14). El dinero y las apuestas viven en SQL Server; los partidos y sus resultados, en
    /// PostgreSQL. Entre los dos gestores NO hay transaccion distribuida, por eso se usa el patron saga / consistencia eventual:
    ///   1. Al terminar un partido se envian al instante los resultados de sus cuotas (LiquidarPartidoAsync).
    ///   2. Si ese envio falla (SQL Server caido, la app se cierra justo ahi...), la RECONCILIACION periodica lo recupera:
    ///      busca en SQL Server las selecciones aun PENDIENTE, mira en PostgreSQL si su partido ya termino (o se anulo) y
    ///      reenvia el resultado.
    /// Repetir cualquiera de las dos cosas es seguro: los procedimientos solo tocan apuestas y selecciones PENDIENTE, asi que
    /// un premio nunca se acredita dos veces.
    /// </summary>
    public sealed class LiquidadorPartidos
    {
        private readonly ILiquidacionRepository _sql;
        private readonly ISimuladorRepository _pg;
        private readonly ILogger<LiquidadorPartidos> _log;

        public LiquidadorPartidos(ILiquidacionRepository sql, ISimuladorRepository pg, ILogger<LiquidadorPartidos> log)
        {
            _sql = sql;
            _pg = pg;
            _log = log;
        }

        /// <summary>Envia a sp_ResolverApuestasPartido el resultado de todas las cuotas de un partido recien finalizado.</summary>
        public async Task LiquidarPartidoAsync(int idPartido, IReadOnlyList<ResultadoCuotaSim> resultados, CancellationToken ct)
        {
            var resumen = await _sql.ResolverAsync(resultados.Select(r => (r.IdCuota, r.Ganadora)).ToList(), ct);
            _log.LogInformation("Liquidacion del partido {IdPartido}: {Ganadas} ganadas, {Perdidas} perdidas, {Anuladas} anuladas.",
                idPartido, resumen.Ganadas, resumen.Perdidas, resumen.Anuladas);
        }

        /// <summary>Busca y liquida lo que haya quedado pendiente de partidos ya FINALIZADO o SUSPENDIDO. Devuelve cuantas apuestas cambiaron.</summary>
        public async Task<int> ReconciliarAsync(CancellationToken ct)
        {
            var pendientes = await _sql.ListarCuotasPendientesAsync(ct);
            if (pendientes.Count == 0) return 0;

            var finalizadas = await _pg.ObtenerResultadosFinalizadosAsync(pendientes, ct);
            var suspendidas = await _pg.ObtenerCuotasDePartidosSuspendidosAsync(pendientes, ct);
            if (finalizadas.Count == 0 && suspendidas.Count == 0) return 0;     // sus partidos aun no terminan

            var total = 0;
            if (finalizadas.Count > 0)
            {
                var r = await _sql.ResolverAsync(finalizadas.Select(x => (x.IdCuota, x.Ganadora)).ToList(), ct);
                total += r.Total;
                _log.LogInformation("Reconciliacion: {N} cuotas de partidos finalizados -> {G} ganadas, {P} perdidas, {A} anuladas.", finalizadas.Count, r.Ganadas, r.Perdidas, r.Anuladas);
            }
            if (suspendidas.Count > 0)
            {
                var r = await _sql.AnularAsync(suspendidas, ct);
                total += r.Total;
                _log.LogInformation("Reconciliacion: {N} cuotas de partidos suspendidos -> {G} ganadas, {P} perdidas, {A} anuladas.", suspendidas.Count, r.Ganadas, r.Perdidas, r.Anuladas);
            }
            return total;
        }
    }
}
