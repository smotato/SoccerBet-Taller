using System.Text.Encodings.Web;

namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>
    /// Correo de recuperacion: HTML sencillo + version de texto. Todo dato que viene de la BD o del usuario (nombre, enlace) se
    /// codifica para HTML. Nunca incluye la contrasena.
    /// </summary>
    public static class PlantillaCorreo
    {
        public const string Asunto = "Restablece tu contraseña de SoccerBet";

        public static (string Html, string Texto) Crear(MensajeRecuperacion m)
        {
            var h = HtmlEncoder.Default;
            var saludo = string.IsNullOrWhiteSpace(m.Nombre) ? "Hola" : "Hola " + m.Nombre.Trim();

            var html =
$@"<!DOCTYPE html>
<html lang=""es""><body style=""margin:0;padding:24px;background:#F6F4F7;font-family:Arial,Helvetica,sans-serif;color:#1E1423"">
  <div style=""max-width:480px;margin:0 auto;background:#ffffff;border-radius:10px;padding:28px"">
    <h2 style=""margin:0 0 16px;color:#2F6A8F"">SoccerBet</h2>
    <p>{h.Encode(saludo)},</p>
    <p>Recibimos una solicitud para restablecer tu contraseña. Pulsa el botón para elegir una nueva:</p>
    <p style=""text-align:center;margin:24px 0""><a href=""{h.Encode(m.Enlace)}"" style=""background:#9ABED6;color:#1E1423;text-decoration:none;font-weight:bold;padding:12px 22px;border-radius:6px;display:inline-block"">Restablecer contraseña</a></p>
    <p style=""font-size:14px"">Este enlace vence en {m.VigenciaMinutos} minutos y solo se puede usar una vez.</p>
    <p style=""font-size:14px;color:#6b6272"">Si el botón no funciona, copia esta dirección en tu navegador:<br>{h.Encode(m.Enlace)}</p>
    <p style=""font-size:14px;color:#6b6272"">Si no pediste esto, ignora este mensaje: tu contraseña no cambiará.</p>
  </div>
</body></html>";

            var texto =
$@"{saludo},

Recibimos una solicitud para restablecer tu contraseña de SoccerBet. Abre este enlace para elegir una nueva:

{m.Enlace}

El enlace vence en {m.VigenciaMinutos} minutos y solo se puede usar una vez.
Si no pediste esto, ignora este mensaje: tu contraseña no cambiará.";

            return (html, texto);
        }
    }
}
