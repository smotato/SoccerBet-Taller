using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Implementacion sobre MongoDB.Driver. El IMongoClient es un singleton compartido (es seguro entre hilos y manejar un
    /// solo cliente es lo recomendado por el driver). Crear la coleccion no abre ninguna conexion: el driver conecta en la
    /// primera operacion.
    /// </summary>
    public class ReglaRepository : IReglaRepository
    {
        private const string Coleccion = "reglas";

        private readonly IMongoCollection<ReglaDocumento> _reglas;

        public ReglaRepository(IMongoClient cliente, IOptions<MongoOptions> opciones)
        {
            _reglas = cliente.GetDatabase(opciones.Value.Database).GetCollection<ReglaDocumento>(Coleccion);
        }

        public async Task<IReadOnlyList<ReglaDocumento>> ListarActivasAsync(CancellationToken ct = default) =>
            await _reglas.Find(r => r.Activa).SortBy(r => r.Orden).ToListAsync(ct);
    }
}
