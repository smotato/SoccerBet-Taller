using System.Globalization;
using System.Text.RegularExpressions;
using CasaApuestas.Data.SqlServer;
using CasaApuestas.Models.ViewModels;
using CasaApuestas.Services.Presentacion;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services
{
    /// <summary>Resultado de pedir un retiro. Codigo: OK, REPETIDO, INVALIDO, SALDO o RECHAZADO.</summary>
    public record ResultadoSolicitudRetiro(bool Exito, string Codigo, string Mensaje);

    public interface IRetiroService
    {
        /// <summary>Datos de la pagina "Retirar": saldo, minimo, medios y "Mis retiros" del cliente.</summary>
        Task<RetirarViewModel> ObtenerPaginaAsync(int idCliente, string? clave = null, string? importeIngresado = null,
            string? medioSeleccionado = null, string? mensaje = null, string? error = null, CancellationToken ct = default);

        Task<ResultadoSolicitudRetiro> RetirarAsync(int idCliente, string? clave, string? importe, string? medio, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso "retiro simulado" (RF16). Se aprueba al instante; no mueve dinero real. TODO se valida aqui, en el
    /// servidor. El saldo y los retiros solo cambian por sp_RetirarSaldo (transaccion + idempotencia en SQL Server).
    ///
    /// Orden de las comprobaciones:
    ///   1. Clave valida.
    ///   2. Idempotencia: si la clave YA creo un retiro de ESTE cliente, se devuelve ese retiro y no se valida ni descuenta
    ///      nada (si se validara el saldo primero, un reenvio legitimo diria "saldo insuficiente" tras haber retirado).
    ///   3. Medio de la lista, importe entero y minimo.
    ///   4. Saldo (comodidad: da un mensaje claro). La autoridad es el procedimiento, que lo vuelve a comprobar.
    ///   5. sp_RetirarSaldo.
    /// </summary>
    public class RetiroService : IRetiroService
    {
        private const int MaxRetirosListados = 50;

        // Medios simulados permitidos (constante del negocio, no un limite). El texto se guarda tal cual en Retiro.medio.
        public static readonly IReadOnlyList<string> Medios = new[] { "Nequi", "PSE", "Cuenta bancaria" };

        // Un GUID con guiones mide 36; el procedimiento acepta hasta 64.
        private static readonly Regex ClaveValida = new("^[A-Za-z0-9-]{8,64}$", RegexOptions.Compiled);

        private readonly IRetiroRepository _retiros;
        private readonly ISaldoService _saldo;
        private readonly RetirosOptions _opc;
        private readonly TimeProvider _reloj;
        private readonly ILogger<RetiroService> _log;

        public RetiroService(IRetiroRepository retiros, ISaldoService saldo, IOptions<RetirosOptions> opciones, TimeProvider reloj, ILogger<RetiroService> log)
        {
            _retiros = retiros;
            _saldo = saldo;
            _opc = opciones.Value;
            _reloj = reloj;
            _log = log;
        }

        public async Task<RetirarViewModel> ObtenerPaginaAsync(int idCliente, string? clave = null, string? importeIngresado = null,
            string? medioSeleccionado = null, string? mensaje = null, string? error = null, CancellationToken ct = default)
        {
            var saldo = await _saldo.ObtenerSaldoAsync(idCliente, ct);
            var retiros = await _retiros.ListarAsync(idCliente, MaxRetirosListados, ct);
            var ahora = FechaColombia.Ahora(_reloj);

            return new RetirarViewModel
            {
                SaldoDisponible = saldo,
                SaldoTexto = Pesos.Formatear(saldo),
                Minimo = _opc.ImporteMinimo,
                MinimoTexto = Pesos.Formatear(_opc.ImporteMinimo),
                // Un GUID por formulario abierto. Si el formulario se vuelve a mostrar tras un error (no se creo retiro),
                // se conserva la misma clave; tras un exito siempre se genera una nueva.
                Clave = clave is not null && ClaveValida.IsMatch(clave) ? clave : Guid.NewGuid().ToString("D"),
                Medios = Medios,
                Retiros = retiros.Select(r => new RetiroItemViewModel(r.IdRetiro, FechaColombia.Formatear(r.Fecha, ahora), r.Medio, r.Estado, Pesos.Formatear(r.Importe))).ToList(),
                ImporteIngresado = importeIngresado,
                MedioSeleccionado = medioSeleccionado,
                Mensaje = mensaje,
                Error = error
            };
        }

        public async Task<ResultadoSolicitudRetiro> RetirarAsync(int idCliente, string? clave, string? importe, string? medio, CancellationToken ct = default)
        {
            // ---- 1. Clave ----
            clave = clave?.Trim();
            if (clave is null || !ClaveValida.IsMatch(clave))
                return Invalido("Falta la clave del formulario. Recarga la página e inténtalo de nuevo.");

            // ---- 2. Idempotencia (siempre por el id_cliente de la SESION) ----
            var previo = await _retiros.BuscarPorClaveAsync(idCliente, clave, ct);
            if (previo is not null)
                return new(true, "REPETIDO", $"Este retiro ya estaba registrado ({Pesos.Formatear(previo.Importe)} a {previo.Medio}). No se descontó de nuevo.");

            // ---- 3. Forma ----
            var medioValido = Medios.FirstOrDefault(m => string.Equals(m, medio?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (medioValido is null) return Invalido("Elige un medio de retiro válido: Nequi, PSE o cuenta bancaria.");

            if (string.IsNullOrWhiteSpace(importe)) return Invalido("Escribe el importe a retirar.");
            if (!decimal.TryParse(importe.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
                return Invalido("El importe no es válido.");
            if (valor != decimal.Truncate(valor)) return Invalido("El importe debe ser en pesos enteros.");
            if (valor <= 0 || valor < _opc.ImporteMinimo)
                return Invalido($"El importe mínimo de retiro es {Pesos.Formatear(_opc.ImporteMinimo)}.");

            // ---- 4. Saldo (aviso claro; la autoridad es el procedimiento) ----
            var saldo = await _saldo.ObtenerSaldoAsync(idCliente, ct);
            if (saldo < valor) return SaldoInsuficiente(saldo, valor);

            // ---- 5. sp_RetirarSaldo ----
            ResultadoRetiro res;
            try
            {
                res = await _retiros.RegistrarAsync(idCliente, valor, medioValido, clave, _opc.ImporteMinimo, ct);
            }
            catch (RetiroRechazadoException ex)
            {
                _log.LogWarning("sp_RetirarSaldo rechazo el retiro del cliente {IdCliente} (error {Numero}).", idCliente, ex.Numero);
                return ex.Numero switch
                {
                    50030 => new(false, "RECHAZADO", $"El importe es menor al mínimo de retiro ({Pesos.Formatear(_opc.ImporteMinimo)})."),
                    50031 => SaldoInsuficiente(await _saldo.ObtenerSaldoAsync(idCliente, ct), valor),
                    50027 => new(false, "RECHAZADO", "Falta la clave del formulario. Recarga la página e inténtalo de nuevo."),
                    50021 => new(false, "RECHAZADO", "Tu cuenta de cliente no existe o no está activa."),
                    _ => new(false, "RECHAZADO", "No pudimos registrar el retiro. Inténtalo de nuevo.")
                };
            }

            // Carrera: dos envios simultaneos con la misma clave. El segundo recibe repetido = 1 del procedimiento.
            if (res.Repetido)
                return new(true, "REPETIDO", $"Este retiro ya estaba registrado ({Pesos.Formatear(valor)} a {medioValido}). No se descontó de nuevo.");

            return new(true, "OK", $"Retiro #{res.IdRetiro} de {Pesos.Formatear(valor)} a {medioValido} aprobado al instante. Es una simulación: no se envía dinero real.");
        }

        private static ResultadoSolicitudRetiro Invalido(string mensaje) => new(false, "INVALIDO", mensaje);

        private static ResultadoSolicitudRetiro SaldoInsuficiente(decimal saldo, decimal importe) =>
            new(false, "SALDO", $"Saldo insuficiente. Tu saldo es {Pesos.Formatear(saldo)} y quieres retirar {Pesos.Formatear(importe)}.");
    }
}
