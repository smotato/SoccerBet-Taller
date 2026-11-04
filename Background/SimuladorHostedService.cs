using CasaApuestas.Data.Postgres;
using CasaApuestas.Services.Simulador;
using Microsoft.Extensions.Options;

namespace CasaApuestas.Background
{
    /// <summary>
    /// Servicio en segundo plano del simulador (RF13). Cada IntervaloCicloSegundos:
    ///   1. intenta ser la instancia ACTIVA (candado de PostgreSQL: solo una instancia simula a la vez);
    ///   2. si lo es, ejecuta un ciclo del simulador.
    /// Un error en un ciclo (Postgres o Mongo caidos, etc.) se registra y se reintenta en el siguiente: nunca detiene la app.
    /// Los servicios se resuelven aqui dentro (no en el constructor) para que, si la configuracion de las bases falla, la app
    /// arranque igual y el resto del sitio siga funcionando.
    /// </summary>
    public sealed class SimuladorHostedService : BackgroundService
    {
        private readonly IServiceProvider _servicios;
        private readonly SimuladorOptions _opc;
        private readonly ILogger<SimuladorHostedService> _log;

        public SimuladorHostedService(IServiceProvider servicios, IOptions<SimuladorOptions> opciones, ILogger<SimuladorHostedService> log)
        {
            _servicios = servicios;
            _opc = opciones.Value;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_opc.Habilitado)
            {
                _log.LogInformation("Simulador de partidos DESACTIVADO (Simulador:Habilitado = false).");
                return;
            }

            _log.LogInformation("Simulador de partidos habilitado: ciclo cada {Ciclo} s, partido de {Duracion} s reales ({Minutos} min), hasta {Max} en vivo, escalon de {Escalon} s, adelantar programados = {Adelantar}.",
                _opc.IntervaloCicloSegundos, _opc.DuracionRealPartidoSegundos, _opc.MinutosPartido, _opc.MaxPartidosEnVivo, _opc.EscalonInicioSegundos, _opc.AdelantarPartidosProgramados);

            BloqueoSimulador? bloqueo = null;
            SimuladorPartidos? simulador = null;
            bool? eraActivo = null;

            try
            {
                using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(_opc.IntervaloCicloSegundos));
                while (await temporizador.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        bloqueo ??= _servicios.GetRequiredService<BloqueoSimulador>();
                        simulador ??= _servicios.GetRequiredService<SimuladorPartidos>();

                        var activo = await bloqueo.AdquirirAsync(stoppingToken);
                        if (activo != eraActivo)
                        {
                            _log.LogInformation(activo
                                ? "Esta instancia es el simulador ACTIVO (tomo el candado de PostgreSQL)."
                                : "Hay otra instancia simulando: esta instancia queda en espera y reintenta cada ciclo.");
                            eraActivo = activo;
                        }
                        if (!activo) continue;

                        await simulador.EjecutarCicloAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Fallo en el ciclo del simulador; se reintentara en {Ciclo} s.", _opc.IntervaloCicloSegundos);
                    }
                }
            }
            catch (OperationCanceledException) { /* apagado normal de la app */ }
            finally
            {
                if (bloqueo is not null) await bloqueo.DisposeAsync();
            }
        }
    }
}
