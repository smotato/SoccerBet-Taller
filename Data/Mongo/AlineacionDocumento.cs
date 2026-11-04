using MongoDB.Bson.Serialization.Attributes;

namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Documento de la coleccion `alineaciones` (ver db/mongo/01_init.js): una alineacion por (partido, equipo, tipo) con los
    /// jugadores anidados. IdPartido -> PostgreSQL.partido; IdEquipo / IdJugador -> MySQL (IDs logicos, sin JOIN entre gestores).
    /// </summary>
    [BsonIgnoreExtraElements]
    public class AlineacionDocumento
    {
        [BsonElement("id_partido")] public int IdPartido { get; set; }
        [BsonElement("id_equipo")] public int IdEquipo { get; set; }

        /// <summary>Por ahora solo "INICIAL" (los 11 titulares).</summary>
        [BsonElement("tipo")] public string Tipo { get; set; } = TipoInicial;

        /// <summary>Por ejemplo "4-4-2". NULL cuando el equipo no tiene posiciones en el catalogo.</summary>
        [BsonElement("formacion")] public string? Formacion { get; set; }

        [BsonElement("jugadores")] public List<AlineacionJugadorDocumento> Jugadores { get; set; } = new();

        public const string TipoInicial = "INICIAL";
    }

    [BsonIgnoreExtraElements]
    public class AlineacionJugadorDocumento
    {
        [BsonElement("id_jugador")] public int IdJugador { get; set; }
        [BsonElement("titular")] public bool Titular { get; set; }
        [BsonElement("numero_camiseta")] public int? NumeroCamiseta { get; set; }
        [BsonElement("posicion")] public string? Posicion { get; set; }
        [BsonElement("minuto_entrada")] public int? MinutoEntrada { get; set; }
        [BsonElement("minuto_salida")] public int? MinutoSalida { get; set; }
    }
}
