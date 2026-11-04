using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>
    /// Envio real por SMTP con MailKit (no el SmtpClient obsoleto): STARTTLS en el puerto configurado y credenciales de
    /// user-secrets. Todo el envio tiene un tiempo limite (RecuperacionOptions.EnvioTimeoutSegundos). Si falla, se registra el
    /// error (sin el enlace ni el token) y se devuelve Enviado = false: el llamador responde igual al usuario.
    /// </summary>
    public class EntregaSmtp : IEntregaEnlace
    {
        private readonly SmtpOptions _smtp;
        private readonly RecuperacionOptions _opc;
        private readonly ILogger<EntregaSmtp> _log;

        public EntregaSmtp(IOptions<SmtpOptions> smtp, IOptions<RecuperacionOptions> opc, ILogger<EntregaSmtp> log)
        {
            _smtp = smtp.Value; _opc = opc.Value; _log = log;
        }

        public async Task<ResultadoEntrega> EntregarAsync(MensajeRecuperacion m, CancellationToken ct)
        {
            if (!_smtp.Habilitado)
            {
                _log.LogWarning("SMTP apagado (Smtp:Habilitado = false): no se envio el correo de recuperacion.");
                return new ResultadoEntrega(false);
            }

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(_opc.EnvioTimeoutSegundos));
            try
            {
                var (html, texto) = PlantillaCorreo.Crear(m);
                var remitente = string.IsNullOrWhiteSpace(_smtp.CorreoRemitente) ? _smtp.Usuario : _smtp.CorreoRemitente;

                var mensaje = new MimeMessage();
                mensaje.From.Add(new MailboxAddress(_smtp.NombreRemitente, remitente));
                mensaje.To.Add(MailboxAddress.Parse(m.Correo));
                mensaje.Subject = PlantillaCorreo.Asunto;
                mensaje.Body = new BodyBuilder { HtmlBody = html, TextBody = texto }.ToMessageBody();

                using var cliente = new SmtpClient { Timeout = _opc.EnvioTimeoutSegundos * 1000 };
                await cliente.ConnectAsync(_smtp.Servidor, _smtp.Puerto, SecureSocketOptions.StartTls, limite.Token);
                await cliente.AuthenticateAsync(_smtp.Usuario, _smtp.Clave, limite.Token);
                await cliente.SendAsync(mensaje, limite.Token);
                await cliente.DisconnectAsync(true, CancellationToken.None);
                return new ResultadoEntrega(true);
            }
            catch (Exception ex)
            {
                // Se registra el tipo de fallo; nunca el enlace ni el token.
                _log.LogError(ex, "No se pudo enviar el correo de recuperacion por SMTP ({Servidor}:{Puerto}).", _smtp.Servidor, _smtp.Puerto);
                return new ResultadoEntrega(false);
            }
        }
    }
}
