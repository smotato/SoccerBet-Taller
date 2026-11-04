namespace CasaApuestas.Data.Mongo
{
    /// <summary>Eventos de partido en MongoDB (goles, tarjetas...). En esta fase el simulador los escribe.</summary>
    public interface IEventoPartidoRepository
    {
        /// <summary>
        /// Guarda los eventos de forma IDEMPOTENTE: cada uno se inserta solo si no existe ya el mismo
        /// (partido, minuto, tipo, equipo, jugador). Repetir la llamada (reintento tras un fallo, reinicio de la app) no
        /// duplica nada. Devuelve cuantos eventos nuevos se insertaron.
        /// </summary>
        Task<int> GuardarAsync(IReadOnlyCollection<EventoPartidoDocumento> eventos, CancellationToken ct = default);

        /// <summary>Los eventos de un partido (solo lectura), sin orden garantizado: el servicio los ordena.</summary>
        Task<IReadOnlyList<EventoPartidoDocumento>> ListarPorPartidoAsync(int idPartido, CancellationToken ct = default);
    }
}
