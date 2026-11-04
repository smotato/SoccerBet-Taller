using CasaApuestas.Data.SqlServer;
using CasaApuestas.Services.Presentacion;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services
{
    /// <summary>Valor de cash out de una apuesta PENDIENTE, tal como lo calcula el servidor.</summary>
    public record ValorCashOut(long IdApuesta, bool Disponible, long Valor, string? Motivo);

    /// <summary>
    /// Resultado de confirmar un cash out. Codigo: OK, REPETIDA (ya estaba cobrada: no se acredito de nuevo), CAMBIO (el valor cambio:
    /// NuevoValor para que el cliente decida; NO se cobro), BLOQUEADO, NO_EXISTE, RECHAZADO.
    /// </summary>
    public class CashOutResultado
    {
        public bool Exito { get; init; }
        public string Codigo { get; init; } = "";
        public string Mensaje { get; init; } = "";
        public long? Valor { get; init; }
        public long? NuevoValor { get; init; }
        public string? SaldoTexto { get; init; }
        public int Status { get; init; }
    }

    public interface ICashOutService
    {
        /// <summary>Valor de cash out de todas las apuestas PENDIENTE del cliente (lo que refresca Mis apuestas).</summary>
        Task<IReadOnlyList<ValorCashOut>> CalcularPendientesAsync(int idCliente, CancellationToken ct = default);

        Task<CashOutResultado> ConfirmarAsync(int idCliente, long idApuesta, long valorVisto, CancellationToken ct = default);
    }

    /// <summary>
    /// Cash out (RF17). El navegador solo MUESTRA: el valor lo calcula siempre el servidor, con las cuotas de PostgreSQL y las
    /// selecciones de SQL Server (siempre filtradas por el cliente de la SESION). Al confirmar:
    ///   1. La apuesta debe ser del cliente y estar PENDIENTE (si ya esta en CASHOUT se devuelve como REPETIDA, sin tocar el saldo).
    ///   2. Se recalcula; si no esta disponible, se rechaza con el motivo.
    ///   3. Si el valor difiere del que vio el cliente, NO se cobra: se devuelve el nuevo para que el cliente lo acepte (o no).
    ///   4. Se llama a sp_CashOut (transaccion + idempotencia por estado en SQL Server; es la autoridad del dinero).
    /// </summary>
    public class CashOutService : ICashOutService
    {
        private readonly IApuestaRepository _apuestas;
        private readonly IContextoCuotasLoader _contexto;
        private readonly ISaldoService _saldo;
        private readonly CashOutOptions _opc;
        private readonly ILogger<CashOutService> _log;

        public CashOutService(IApuestaRepository apuestas, IContextoCuotasLoader contexto, ISaldoService saldo, IOptions<CashOutOptions> opciones, ILogger<CashOutService> log)
        {
            _apuestas = apuestas;
            _contexto = contexto;
            _saldo = saldo;
            _opc = opciones.Value;
            _log = log;
        }

        public async Task<IReadOnlyList<ValorCashOut>> CalcularPendientesAsync(int idCliente, CancellationToken ct = default)
        {
            var filas = await _apuestas.ListarApuestasDeClienteAsync(idCliente, ct);
            var pendientes = filas.Where(f => f.EstadoApuesta == "PENDIENTE").GroupBy(f => f.IdApuesta).ToList();
            if (pendientes.Count == 0) return Array.Empty<ValorCashOut>();

            var ctx = await _contexto.CargarAsync(pendientes.SelectMany(g => g).Where(f => f.EstadoSeleccion == "PENDIENTE").Select(f => f.IdCuota).Distinct().ToList(), ct);
            return pendientes.Select(g =>
            {
                var r = CalculadoraCashOut.Evaluar(g.OrderBy(f => f.IdDetalle).ToList(), ctx, _opc.Factor);
                return new ValorCashOut(g.Key, r.Disponible, r.Valor, r.Motivo);
            }).ToList();
        }

        public async Task<CashOutResultado> ConfirmarAsync(int idCliente, long idApuesta, long valorVisto, CancellationToken ct = default)
        {
            // 1. Solo se ven las apuestas DEL CLIENTE DE LA SESION: pedir la de otro cliente es "no existe".
            var filas = (await _apuestas.ListarApuestasDeClienteAsync(idCliente, ct)).Where(f => f.IdApuesta == idApuesta).OrderBy(f => f.IdDetalle).ToList();
            if (filas.Count == 0) return Falla("NO_EXISTE", 404, "La apuesta no existe.");

            var estado = filas[0].EstadoApuesta;
            if (estado == "CASHOUT")
                return await ExitoAsync(idCliente, "REPETIDA", "Esta apuesta ya estaba cobrada con cash out. No se acreditó de nuevo.", filas[0].ValorCashout, ct);
            if (estado != "PENDIENTE")
                return Falla("RECHAZADO", 422, "La apuesta ya se resolvió; no se puede hacer cash out.");

            // 2. Recalculo con las cuotas de PostgreSQL (si esta caido, la excepcion sube y el controlador responde 503: no se cobra).
            var ctx = await _contexto.CargarAsync(filas.Where(f => f.EstadoSeleccion == "PENDIENTE").Select(f => f.IdCuota).Distinct().ToList(), ct);
            var calc = CalculadoraCashOut.Evaluar(filas, ctx, _opc.Factor);
            if (!calc.Disponible) return Falla("BLOQUEADO", 422, calc.Motivo ?? "El cash out no está disponible ahora.");

            // 3. Si el valor cambio desde que el cliente lo vio, no se cobra: el cliente decide con el valor nuevo.
            if (calc.Valor != valorVisto)
                return new CashOutResultado
                {
                    Exito = false, Codigo = "CAMBIO", Status = 409, NuevoValor = calc.Valor,
                    Mensaje = $"El valor de tu cash out cambió de {Pesos.Formatear(valorVisto)} a {Pesos.Formatear(calc.Valor)}. Revisa el nuevo valor y confirma si quieres cobrarlo."
                };

            // 4. sp_CashOut: marca CASHOUT solo si sigue PENDIENTE y acredita UNA vez.
            try
            {
                var repetido = await _apuestas.CashOutAsync(idCliente, idApuesta, calc.Valor, ct);
                return repetido
                    ? await ExitoAsync(idCliente, "REPETIDA", "Esta apuesta ya estaba cobrada con cash out. No se acreditó de nuevo.", null, ct)
                    : await ExitoAsync(idCliente, "OK", $"Cash out realizado: se acreditaron {Pesos.Formatear(calc.Valor)} a tu saldo.", calc.Valor, ct);
            }
            catch (CashOutRechazadoException ex)
            {
                _log.LogWarning("sp_CashOut rechazo el cash out de la apuesta {IdApuesta} del cliente {IdCliente} (error {Numero}).", idApuesta, idCliente, ex.Numero);
                return ex.Numero switch
                {
                    50032 => Falla("RECHAZADO", 422, "El valor de cash out es menor a $ 1."),
                    50033 => Falla("NO_EXISTE", 404, "La apuesta no existe."),
                    50034 => Falla("RECHAZADO", 422, "La apuesta ya se resolvió; no se puede hacer cash out."),
                    50012 => Falla("RECHAZADO", 422, "Tu cuenta no tiene saldo creado."),
                    _ => Falla("RECHAZADO", 422, "No pudimos hacer el cash out. Inténtalo de nuevo.")
                };
            }
        }

        private async Task<CashOutResultado> ExitoAsync(int idCliente, string codigo, string mensaje, decimal? valor, CancellationToken ct)
        {
            string? saldoTexto = null;
            try { saldoTexto = Pesos.Formatear(await _saldo.ObtenerSaldoAsync(idCliente, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "No se pudo leer el saldo tras el cash out."); }
            return new CashOutResultado { Exito = true, Codigo = codigo, Status = 200, Mensaje = mensaje, Valor = valor is null ? null : (long)valor, SaldoTexto = saldoTexto };
        }

        private static CashOutResultado Falla(string codigo, int status, string mensaje) =>
            new() { Exito = false, Codigo = codigo, Status = status, Mensaje = mensaje };
    }
}
