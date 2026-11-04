namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>Lo que hace falta para avisar al usuario: a quien, como saludarlo y el enlace de un solo uso.</summary>
    public record MensajeRecuperacion(string Correo, string? Nombre, string Enlace, int VigenciaMinutos);

    /// <summary>Enviado = el mensaje salio por un canal real (correo). EnlaceParaMostrar solo lo llena la entrega de Development.</summary>
    public record ResultadoEntrega(bool Enviado, string? EnlaceParaMostrar = null);

    /// <summary>
    /// Como se le entrega el enlace al usuario. Dos implementaciones: EntregaSmtp (correo real, MailKit) y
    /// EntregaEnPantallaDevelopment (solo se registra en Development). Nunca debe lanzar excepciones hacia el llamador.
    /// </summary>
    public interface IEntregaEnlace
    {
        Task<ResultadoEntrega> EntregarAsync(MensajeRecuperacion mensaje, CancellationToken ct);
    }
}
