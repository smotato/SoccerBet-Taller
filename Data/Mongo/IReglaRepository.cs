namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Lectura de las reglas guardadas en MongoDB. Los servicios solo conocen esta interfaz, no que el gestor sea Mongo.
    /// SOLO LECTURA: esta fase no escribe nada en Mongo.
    /// </summary>
    public interface IReglaRepository
    {
        /// <summary>Reglas con activa = true, ordenadas por orden. Agruparlas por categoria es trabajo del servicio.</summary>
        Task<IReadOnlyList<ReglaDocumento>> ListarActivasAsync(CancellationToken ct = default);
    }
}
