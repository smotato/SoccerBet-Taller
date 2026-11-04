using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Alineaciones con el usuario soccerbet_app (readWrite sobre la base soccerbet; nunca root). Escribe con upsert +
    /// $setOnInsert sobre la clave natural (id_partido, id_equipo, tipo), la misma del indice unico de la coleccion.
    /// </summary>
    public class AlineacionRepository : IAlineacionRepository
    {
        private readonly IMongoCollection<AlineacionDocumento> _coleccion;

        public AlineacionRepository(IMongoClient cliente, IOptions<MongoOptions> opciones)
        {
            _coleccion = cliente.GetDatabase(opciones.Value.Database).GetCollection<AlineacionDocumento>(opciones.Value.ColeccionAlineaciones);
        }

        public async Task<IReadOnlyList<AlineacionDocumento>> ObtenerPorPartidoAsync(int idPartido, CancellationToken ct = default) =>
            await _coleccion.Find(a => a.IdPartido == idPartido).ToListAsync(ct);

        public async Task<int> GuardarAsync(IReadOnlyCollection<AlineacionDocumento> alineaciones, CancellationToken ct = default)
        {
            if (alineaciones.Count == 0) return 0;

            var f = Builders<AlineacionDocumento>.Filter;
            var u = Builders<AlineacionDocumento>.Update;

            var operaciones = alineaciones.Select(a => (WriteModel<AlineacionDocumento>)new UpdateOneModel<AlineacionDocumento>(
                f.And(f.Eq(x => x.IdPartido, a.IdPartido), f.Eq(x => x.IdEquipo, a.IdEquipo), f.Eq(x => x.Tipo, a.Tipo)),
                u.SetOnInsert(x => x.Formacion, a.Formacion)
                 .SetOnInsert(x => x.Jugadores, a.Jugadores))
            { IsUpsert = true }).ToList();

            var r = await _coleccion.BulkWriteAsync(operaciones, new BulkWriteOptions { IsOrdered = false }, ct);
            return (int)r.Upserts.Count;
        }

        public async Task<IReadOnlySet<int>> PartidosConAlineacionAsync(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default)
        {
            if (idsPartido.Count == 0) return new HashSet<int>();
            var ids = await _coleccion.Distinct(a => a.IdPartido, Builders<AlineacionDocumento>.Filter.In(a => a.IdPartido, idsPartido)).ToListAsync(ct);
            return ids.ToHashSet();
        }
    }
}
