using MongoDB.Bson.Serialization.Attributes;

namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Documento de la coleccion `eventos_partido` (ver db/mongo/01_init.js, que valida estos campos).
    /// Los ids son IDs LOGICOS hacia otros gestores: IdPartido -> PostgreSQL.partido, IdEquipo / IdJugador -> MySQL.
    /// No hay navegacion ni JOIN entre gestores.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class EventoPartidoDocumento
    {
        [BsonElement("id_partido")] public int IdPartido { get; set; }
        [BsonElement("id_equipo")] public int? IdEquipo { get; set; }
        [BsonElement("id_jugador")] public int? IdJugador { get; set; }

        /// <summary>INICIO, GOL, TARJETA_AMARILLA, TARJETA_ROJA o FIN.</summary>
        [BsonElement("tipo_evento")] public string TipoEvento { get; set; } = "";

        [BsonElement("minuto")] public int Minuto { get; set; }
        [BsonElement("minuto_adicional")] public int? MinutoAdicional { get; set; }
        [BsonElement("descripcion")] public string? Descripcion { get; set; }

        [BsonElement("creado")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime Creado { get; set; }
    }
}
