namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>
    /// SOLO Development (Program.cs la registra unicamente en ese entorno): si el SMTP esta apagado o falla, deja el enlace
    /// disponible para mostrarlo en pantalla y poder probar sin correo. En produccion esta clase no se registra.
    /// </summary>
    public class EntregaEnPantallaDevelopment : IEntregaEnlace
    {
        public Task<ResultadoEntrega> EntregarAsync(MensajeRecuperacion mensaje, CancellationToken ct) =>
            Task.FromResult(new ResultadoEntrega(false, mensaje.Enlace));
    }
}
