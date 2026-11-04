using System.Security.Cryptography;
using System.Text;
using CasaApuestas.Data.SqlServer;
using CasaApuestas.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>EnlaceDevelopment solo se llena en Development cuando el correo no salio por SMTP; en cualquier otro caso es null.</summary>
    public record SolicitudRecuperacion(string? EnlaceDevelopment);

    public interface IRecuperacionService
    {
        /// <summary>La respuesta al usuario NO depende de si el correo existe: este metodo no devuelve esa informacion.</summary>
        Task<SolicitudRecuperacion> SolicitarAsync(string correo, string? ip, CancellationToken ct = default);

        /// <summary>true si el token existe, esta vigente (no usado, no vencido) y coincide (comparacion en tiempo constante).</summary>
        Task<bool> EnlaceValidoAsync(string? token, CancellationToken ct = default);

        /// <summary>Cambia la contrasena si el token es valido; false si no (vencido, usado o incorrecto).</summary>
        Task<bool> RestablecerAsync(string? token, string nuevaContrasena, CancellationToken ct = default);
    }

    /// <summary>
    /// Caso de uso RF15. El codigo (32 bytes aleatorios, base64url) viaja SOLO en el enlace del correo; en SQL Server se guarda
    /// su hash SHA-256. El token nunca se registra en logs. Orden de la solicitud: limite por correo -> codigo -> procedimiento
    /// (crea e invalida el anterior) -> entrega (segundo plano; en Development, en la misma peticion).
    /// </summary>
    public class RecuperacionService : IRecuperacionService
    {
        private static readonly PasswordHasher<Login> Hasher = new();

        private readonly IRecuperacionRepository _repo;
        private readonly RecuperacionOptions _opc;
        private readonly EnviadorRecuperacion _enviador;
        private readonly ColaCorreosRecuperacion _cola;
        private readonly IMemoryCache _cache;
        private readonly IHostEnvironment _entorno;
        private readonly ILogger<RecuperacionService> _log;

        public RecuperacionService(IRecuperacionRepository repo, IOptions<RecuperacionOptions> opc, EnviadorRecuperacion enviador,
            ColaCorreosRecuperacion cola, IMemoryCache cache, IHostEnvironment entorno, ILogger<RecuperacionService> log)
        {
            _repo = repo; _opc = opc.Value; _enviador = enviador; _cola = cola; _cache = cache; _entorno = entorno; _log = log;
        }

        public async Task<SolicitudRecuperacion> SolicitarAsync(string correo, string? ip, CancellationToken ct = default)
        {
            var email = correo.Trim().ToLowerInvariant();

            // Limite por correo (3 por hora), tambien para correos inexistentes: se cuenta por el hash del correo, no por la cuenta.
            if (!PermitirPorCorreo(email)) return new SolicitudRecuperacion(null);

            var codigo = GenerarCodigo();
            var solicitud = await _repo.SolicitarAsync(email, Hash(codigo), _opc.VigenciaMinutos, ip, _opc.MaxSolicitudesPorCorreoPorHora, ct);
            if (!solicitud.Creada) return new SolicitudRecuperacion(null);      // correo inexistente o limite en BD: misma respuesta

            var enlace = $"{_opc.BaseUrl.TrimEnd('/')}/Recuperacion/Restablecer?t={solicitud.IdRecuperacion}.{codigo}";
            var mensaje = new MensajeRecuperacion(email, solicitud.Nombre, enlace, _opc.VigenciaMinutos);

            if (_entorno.IsDevelopment())
            {
                // Development: en la misma peticion (con tiempo limite), para poder mostrar el enlace si el SMTP falla o esta apagado.
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limite.CancelAfter(TimeSpan.FromSeconds(_opc.EnvioTimeoutSegundos + 2));
                var r = await _enviador.EnviarAsync(mensaje, limite.Token);
                return new SolicitudRecuperacion(r.Enviado ? null : r.EnlaceParaMostrar);
            }

            _cola.Encolar(mensaje);                    // produccion: segundo plano, la respuesta no espera al SMTP
            return new SolicitudRecuperacion(null);
        }

        public async Task<bool> EnlaceValidoAsync(string? token, CancellationToken ct = default)
        {
            if (!TryParse(token, out var id, out var codigo)) return false;
            var guardado = await _repo.ObtenerHashVigenteAsync(id, ct);
            return guardado is not null && IgualesEnTiempoConstante(guardado, Hash(codigo));
        }

        public async Task<bool> RestablecerAsync(string? token, string nuevaContrasena, CancellationToken ct = default)
        {
            if (!TryParse(token, out var id, out var codigo)) return false;
            var hash = Hash(codigo);
            var guardado = await _repo.ObtenerHashVigenteAsync(id, ct);
            if (guardado is null || !IgualesEnTiempoConstante(guardado, hash)) return false;

            // Misma tecnica que el registro: PasswordHasher de ASP.NET. La contrasena en claro nunca llega a la BD.
            var passwordHash = Hasher.HashPassword(new Login(), nuevaContrasena);
            // El procedimiento vuelve a validar y consume el codigo de forma atomica (un doble envio solo gana una vez).
            return await _repo.RestablecerAsync(id, hash, passwordHash, ct);
        }

        // ---- utilidades -------------------------------------------------------------------------------------------------

        private bool PermitirPorCorreo(string email)
        {
            var clave = "recuperacion:correo:" + Hash(email);
            var contador = _cache.GetOrCreate(clave, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return new int[1];
            })!;
            lock (contador) return ++contador[0] <= _opc.MaxSolicitudesPorCorreoPorHora;
        }

        public static string GenerarCodigo() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        public static string Hash(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();

        private static bool IgualesEnTiempoConstante(string a, string b) =>
            a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

        /// <summary>Formato del token: "{id}.{codigo}" con codigo de 43 caracteres base64url. Cualquier otra cosa se rechaza.</summary>
        public static bool TryParse(string? token, out long id, out string codigo)
        {
            id = 0; codigo = "";
            if (string.IsNullOrEmpty(token) || token.Length > 70) return false;
            var punto = token.IndexOf('.');
            if (punto <= 0 || !long.TryParse(token.AsSpan(0, punto), out id) || id <= 0) return false;
            codigo = token[(punto + 1)..];
            return codigo.Length == 43 && codigo.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        }
    }
}
