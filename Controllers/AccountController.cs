using CasaApuestas.Data;
using CasaApuestas.Models;
using CasaApuestas.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;

namespace CasaApuestas.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;

        // Hasher estandar de ASP.NET (PBKDF2 con sal aleatoria). Es sin estado, por eso puede ser estatico.
        // Se tipa con Login porque el hasher recibe "el usuario" solo como parametro generico.
        private static readonly PasswordHasher<Login> _hasher = new();

        // Intentos fallidos consecutivos que bloquean la cuenta (Login.bloqueado = 1).
        private const int MaxIntentosFallidos = 5;

        // Nombre del claim con el id del cliente (ClaimTypes no tiene uno estandar para esto).
        public const string ClaimIdCliente = "id_cliente";

        // Mensaje unico para correo inexistente o contrasena incorrecta: no revela cual de los dos fallo.
        private const string MensajeCredencialesInvalidas = "Datos Incorrectos!";

        public AccountController(ApplicationDbContext context, ILogger<AccountController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // returnUrl (tambien llega como ?ReturnUrl= desde [Authorize] y desde el cupon): SOLO se acepta una URL local
            // (Url.IsLocalUrl rechaza "//otro-sitio", "http://otro-sitio" y "/\otro-sitio"). Cualquier otra cosa se ignora,
            // para que nadie pueda usar el login como redireccion abierta hacia un sitio externo.
            var destino = Url.IsLocalUrl(returnUrl) ? returnUrl : null;

            // Quien ya tiene sesion no necesita ver el login.
            if (User.Identity?.IsAuthenticated == true)
                return destino is not null ? LocalRedirect(destino) : RedirectToAction("Index", "Home");

            ViewData["ReturnUrl"] = destino;
            return View();
        }

        // Cierra la sesion: borra la cookie. Solo POST con token antiforgery, para que un enlace
        // o imagen en otro sitio no pueda cerrar la sesion de alguien. Es idempotente (sin sesion tampoco falla).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        // Endpoint llamado por AJAX (fetch), no un POST tradicional de formulario.
        [HttpPost]
        [ValidateAntiForgeryToken] // el token llega en el encabezado RequestVerificationToken (ver Login.cshtml)
        public async Task<IActionResult> ValidarLogin([FromBody] LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { exito = false, mensaje = MensajeCredencialesInvalidas });
            }

            // El correo es el username y se guarda en minusculas al registrar (ver Registro).
            var username = model.Username.Trim().ToLowerInvariant();

            // Login se consulta CON seguimiento porque este metodo si lo actualiza (contador de intentos, ultimo_login).
            // Cliente y Usuario se consultan con AsNoTracking: aqui solo se leen.
            var login = await _context.Logins.FirstOrDefaultAsync(l => l.Username == username);

            // 1) Correo inexistente: mismo mensaje que una clave incorrecta. No hay contador que sumar.
            if (login == null)
            {
                return Json(new { exito = false, mensaje = MensajeCredencialesInvalidas });
            }

            // 2) Clave incorrecta: se suma el intento y, al llegar al limite, se bloquea la cuenta.
            //    Si la cuenta ya estaba bloqueada no se suma mas. El mensaje sigue siendo el mismo
            //    (tampoco se avisa del bloqueo a quien no conoce la clave).
            if (!ContrasenaCorrecta(login, model.Password))
            {
                if (!login.Bloqueado)
                {
                    login.IntentosFallidos++;
                    if (login.IntentosFallidos >= MaxIntentosFallidos)
                    {
                        login.Bloqueado = true;
                    }
                    await _context.SaveChangesAsync();
                }

                return Json(new { exito = false, mensaje = MensajeCredencialesInvalidas });
            }

            // 3) Clave correcta, pero la cuenta esta bloqueada: no entra y el contador no se reinicia.
            if (login.Bloqueado)
            {
                return Json(new { exito = false, mensaje = "Cuenta bloqueada." });
            }

            // 4) El cliente debe existir y estar ACTIVO (Cliente.estado).
            var cliente = await _context.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdUsuario == login.IdUsuario);

            if (cliente == null || cliente.Estado != "ACTIVO")
            {
                return Json(new { exito = false, mensaje = "Tu cuenta de cliente no está activa." });
            }

            var usuario = await _context.Usuarios.AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == login.IdUsuario);

            if (usuario == null)
            {
                _logger.LogError("El login {IdLogin} apunta a un usuario inexistente ({IdUsuario}).", login.IdLogin, login.IdUsuario);
                return Json(new { exito = false, mensaje = MensajeCredencialesInvalidas });
            }

            // 5) Acierto: se reinicia el contador y se registra el ultimo acceso.
            login.IntentosFallidos = 0;
            login.UltimoLogin = DateTime.Now;
            await _context.SaveChangesAsync();

            // 6) Se emite la cookie de sesion con los datos del usuario como claims.
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                new(ClaimIdCliente, cliente.IdCliente.ToString()),
                new(ClaimTypes.Name, $"{usuario.Nombres} {usuario.Apellidos}".Trim()),
                new(ClaimTypes.Email, usuario.Email)
            };

            var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identidad));

            return Json(new { exito = true, mensaje = "Login Exitoso!" });
        }

        // Verifica la contrasena contra el hash guardado.
        // VerifyHashedPassword LANZA FormatException (no devuelve Failed) si el valor guardado no es base64,
        // como las contrasenas viejas en texto plano. Se trata como clave incorrecta: esos usuarios se recrean con el registro.
        private bool ContrasenaCorrecta(Login login, string password)
        {
            try
            {
                return _hasher.VerifyHashedPassword(login, login.PasswordHash, password) != PasswordVerificationResult.Failed;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        [HttpGet]
        public IActionResult Registro()
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");

            return View();
        }

        // RF2. Tambien por AJAX: responde JSON { exito, mensaje, errores }.
        [HttpPost]
        [ValidateAntiForgeryToken] // el token llega en el encabezado RequestVerificationToken (ver Registro.cshtml)
        public async Task<IActionResult> Registro([FromBody] RegistroViewModel? model)
        {
            // model es null si el cuerpo JSON no se pudo leer (por ejemplo, una fecha con formato invalido).
            if (model == null || !ModelState.IsValid)
            {
                // Un mensaje por campo (el primero), con el nombre de la propiedad como clave:
                // la vista lo muestra debajo del input correspondiente. La clave puede venir con prefijo ("model.Email").
                var errores = ModelState
                    .Where(kv => kv.Value != null && kv.Value.Errors.Any(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)))
                    .ToDictionary(
                        kv => kv.Key.Contains('.') ? kv.Key[(kv.Key.LastIndexOf('.') + 1)..] : kv.Key,
                        kv => kv.Value!.Errors.First(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)).ErrorMessage);

                return Json(new { exito = false, mensaje = "Revisa los datos del formulario.", errores });
            }

            // El correo se normaliza y se usa tambien como username (Login.Username).
            var email = model.Email.Trim().ToLowerInvariant();
            var telefono = string.IsNullOrWhiteSpace(model.Telefono) ? null : model.Telefono.Trim();

            // El hash se genera aqui; la contrasena en claro nunca llega a la base de datos.
            var passwordHash = _hasher.HashPassword(new Login(), model.Password);

            var idCliente = new SqlParameter("@id_cliente", SqlDbType.Int) { Direction = ParameterDirection.Output };

            try
            {
                // sp_RegistrarCliente inserta Usuario, Usuario_Estado, Cliente, Login y Saldo en una sola transaccion.
                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC dbo.sp_RegistrarCliente @nombres, @apellidos, @email, @telefono, @documento, " +
                    "@fecha_nacimiento, @username, @password_hash, @id_cliente OUTPUT",
                    new SqlParameter("@nombres", model.Nombres.Trim()),
                    new SqlParameter("@apellidos", model.Apellidos.Trim()),
                    new SqlParameter("@email", email),
                    new SqlParameter("@telefono", (object?)telefono ?? DBNull.Value),
                    new SqlParameter("@documento", model.Documento.Trim()),
                    new SqlParameter("@fecha_nacimiento", SqlDbType.Date) { Value = model.FechaNacimiento!.Value.Date },
                    new SqlParameter("@username", email),
                    new SqlParameter("@password_hash", passwordHash),
                    idCliente);

                return Json(new { exito = true, mensaje = "Cuenta creada correctamente. Ya puedes iniciar sesión." });
            }
            catch (SqlException ex)
            {
                // Numeros de error definidos con THROW dentro del procedimiento.
                // Cada error conocido se asocia a su campo para mostrarlo debajo del input.
                var (campo, mensaje) = ex.Number switch
                {
                    50001 => ("FechaNacimiento", "Debes ser mayor de 18 años para registrarte."),
                    50002 => ("Email", "Ya existe una cuenta con ese correo electrónico."),
                    50003 => ("Documento", "Ya existe un cliente registrado con ese documento."),
                    50004 => ("Email", "Ese correo ya está en uso como nombre de usuario."),
                    _ => ((string?)null, (string?)null)
                };

                if (campo != null)
                {
                    return Json(new { exito = false, mensaje = "Revisa los datos del formulario.", errores = new Dictionary<string, string> { [campo] = mensaje! } });
                }

                // Error inesperado: se registra el detalle en el log y al usuario solo se le da un mensaje generico.
                _logger.LogError(ex, "Error inesperado en sp_RegistrarCliente (numero {Numero}).", ex.Number);
                return Json(new { exito = false, mensaje = "No se pudo completar el registro. Inténtalo de nuevo más tarde." });
            }
        }
    }
}
