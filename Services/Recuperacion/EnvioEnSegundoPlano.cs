using System.Threading.Channels;

namespace CasaApuestas.Services.Recuperacion
{
    /// <summary>
    /// Prueba cada IEntregaEnlace en orden hasta que una envie. En produccion solo esta EntregaSmtp; en Development tambien la
    /// entrega "en pantalla", que no envia pero deja el enlace para mostrarlo.
    /// </summary>
    public class EnviadorRecuperacion
    {
        private readonly IEnumerable<IEntregaEnlace> _entregas;
        public EnviadorRecuperacion(IEnumerable<IEntregaEnlace> entregas) => _entregas = entregas;

        public async Task<ResultadoEntrega> EnviarAsync(MensajeRecuperacion m, CancellationToken ct)
        {
            string? enlace = null;
            foreach (var entrega in _entregas)
            {
                var r = await entrega.EntregarAsync(m, ct);
                if (r.Enviado) return r;
                enlace ??= r.EnlaceParaMostrar;
            }
            return new ResultadoEntrega(false, enlace);
        }
    }

    /// <summary>Cola en memoria de correos por enviar (acotada: si se llena, descarta el mas viejo).</summary>
    public class ColaCorreosRecuperacion
    {
        private readonly Channel<MensajeRecuperacion> _canal = Channel.CreateBounded<MensajeRecuperacion>(
            new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public void Encolar(MensajeRecuperacion m) => _canal.Writer.TryWrite(m);
        public IAsyncEnumerable<MensajeRecuperacion> LeerAsync(CancellationToken ct) => _canal.Reader.ReadAllAsync(ct);
    }
}

namespace CasaApuestas.Background
{
    using CasaApuestas.Services.Recuperacion;

    /// <summary>
    /// Envia los correos de recuperacion FUERA de la peticion web (fuera de Development). Asi la respuesta tarda lo mismo
    /// exista o no el correo: quien consulta no puede deducir si la cuenta existe por el tiempo de respuesta.
    /// Un fallo se registra y se descarta; nunca afecta al usuario.
    /// </summary>
    public class EnvioCorreosHostedService : BackgroundService
    {
        private readonly ColaCorreosRecuperacion _cola;
        private readonly EnviadorRecuperacion _enviador;
        private readonly ILogger<EnvioCorreosHostedService> _log;

        public EnvioCorreosHostedService(ColaCorreosRecuperacion cola, EnviadorRecuperacion enviador, ILogger<EnvioCorreosHostedService> log)
        {
            _cola = cola; _enviador = enviador; _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var mensaje in _cola.LeerAsync(stoppingToken))
                {
                    try { await _enviador.EnviarAsync(mensaje, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.LogError(ex, "Fallo inesperado al enviar un correo de recuperacion.");
                    }
                }
            }
            catch (OperationCanceledException) { /* la app se esta cerrando */ }
        }
    }
}
