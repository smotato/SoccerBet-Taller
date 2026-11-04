using MongoDB.Bson.Serialization.Attributes;

namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Documento de la coleccion `reglas` (base soccerbet). Modelo PROPIO de MongoDB: no tiene navegacion hacia otros
    /// gestores; IdDeporte es un ID logico hacia MySQL.deporte. SOLO LECTURA en esta fase.
    /// Los nombres de los campos coinciden exactamente con db/mongo/02_seed_reglas.js. Si en la coleccion hay campos que
    /// este modelo no conoce (por ejemplo _id), se ignoran.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class ReglaDocumento
    {
        [BsonElement("id_deporte")] public int IdDeporte { get; set; }
        [BsonElement("nombre")] public string Nombre { get; set; } = "";
        [BsonElement("descripcion")] public string Descripcion { get; set; } = "";
        [BsonElement("activa")] public bool Activa { get; set; }

        /// <summary>'DEPORTE' (reglas del futbol) o 'APUESTAS' (reglas de SoccerBet).</summary>
        [BsonElement("categoria")] public string? Categoria { get; set; }

        /// <summary>Posicion de la regla dentro de su categoria.</summary>
        [BsonElement("orden")] public int Orden { get; set; }
    }
}
