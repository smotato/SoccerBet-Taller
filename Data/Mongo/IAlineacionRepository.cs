namespace CasaApuestas.Data.Mongo
{
    /// <summary>Alineaciones en MongoDB. Solo el simulador las escribe; la pagina de detalle las lee.</summary>
    public interface IAlineacionRepository
    {
        /// <summary>Las alineaciones guardadas de un partido (0, 1 o 2: una por equipo).</summary>
        Task<IReadOnlyList<AlineacionDocumento>> ObtenerPorPartidoAsync(int idPartido, CancellationToken ct = default);

        /// <summary>
        /// Guarda de forma IDEMPOTENTE: upsert por (id_partido, id_equipo, tipo) con $setOnInsert. Si ya existe, NO se modifica
        /// (una alineacion no cambia por repetir la llamada). Devuelve cuantas se insertaron de verdad.
        /// </summary>
        Task<int> GuardarAsync(IReadOnlyCollection<AlineacionDocumento> alineaciones, CancellationToken ct = default);

        /// <summary>De los partidos indicados, los que ya tienen alguna alineacion (para el relleno de una sola vez).</summary>
        Task<IReadOnlySet<int>> PartidosConAlineacionAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default);
    }
}
