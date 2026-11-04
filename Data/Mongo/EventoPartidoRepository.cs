using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Escritura en la coleccion de eventos con el usuario soccerbet_app (rol readWrite sobre la base soccerbet; nunca root).
    /// Usa "upsert": en vez de insertar a ciegas, cada evento es un UpdateOne con $setOnInsert sobre su clave natural. Asi la
    /// idempotencia no depende de crear un indice unico en tu Mongo.
    /// </summary>
    public class EventoPartidoRepository : IEventoPartidoRepository
    {
        private readonly IMongoCollection<EventoPartidoDocumento> _eventos;

        public EventoPartidoRepository(IMongoClient cliente, IOptions<MongoOptions> opciones)
        {
            _eventos = cliente.GetDatabase(opciones.Value.Database).GetCollection<EventoPartidoDocumento>(opciones.Value.ColeccionEventos);
        }

        public async Task<int> GuardarAsync(IReadOnlyCollection<EventoPartidoDocumento> eventos, CancellationToken ct = default)
        {
            if (eventos.Count == 0) return 0;

            var f = Builders<EventoPartidoDocumento>.Filter;
            var u = Builders<EventoPartidoDocumento>.Update;

            var operaciones = eventos.Select(e => (WriteModel<EventoPartidoDocumento>)new UpdateOneModel<EventoPartidoDocumento>(
                f.And(
                    f.Eq(x => x.IdPartido, e.IdPartido),
                    f.Eq(x => x.Minuto, e.Minuto),
                    f.Eq(x => x.TipoEvento, e.TipoEvento),
                    f.Eq(x => x.IdEquipo, e.IdEquipo),
                    f.Eq(x => x.IdJugador, e.IdJugador)),
                u.SetOnInsert(x => x.MinutoAdicional, e.MinutoAdicional)
                 .SetOnInsert(x => x.Descripcion, e.Descripcion)
                 .SetOnInsert(x => x.Creado, e.Creado))
            { IsUpsert = true }).ToList();

            var resultado = await _eventos.BulkWriteAsync(operaciones, new BulkWriteOptions { IsOrdered = false }, ct);
            return (int)resultado.Upserts.Count;
        }
    }
}
