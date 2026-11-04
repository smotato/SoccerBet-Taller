namespace CasaApuestas.Data.Mongo
{
    /// <summary>
    /// Opciones de MongoDB (seccion "Mongo" de appsettings.json). La CADENA DE CONEXION no va aqui: esta en user-secrets
    /// (ConnectionStrings:Mongo). Si falta algun valor la app no arranca, con un mensaje claro.
    /// </summary>
    public class MongoOptions
    {
        public const string Seccion = "Mongo";

        /// <summary>Base de datos (soccerbet).</summary>
        public string Database { get; set; } = "";

        /// <summary>
        /// Segundos que el driver espera para encontrar el servidor antes de fallar. El valor por defecto del driver son
        /// 30 s: con Mongo caido la pagina se quedaria colgada medio minuto, por eso se usa uno corto.
        /// </summary>
        public int ServerSelectionTimeoutSeconds { get; set; }

        /// <summary>Minutos que las reglas se guardan en memoria. 0 = sin cache.</summary>
        public int CacheMinutos { get; set; }

        /// <summary>
        /// Coleccion donde el simulador guarda los eventos de los partidos (eventos_partido). Es configurable para poder
        /// probar el simulador en una coleccion aparte sin mezclar eventos de prueba con los reales.
        /// </summary>
        public string ColeccionEventos { get; set; } = "";

        /// <summary>Coleccion de alineaciones (alineaciones). Configurable por la misma razon que ColeccionEventos.</summary>
        public string ColeccionAlineaciones { get; set; } = "";

        public static bool EsValida(MongoOptions o) =>
            !string.IsNullOrWhiteSpace(o.Database) && o.ServerSelectionTimeoutSeconds > 0 && o.CacheMinutos >= 0
            && !string.IsNullOrWhiteSpace(o.ColeccionEventos) && !string.IsNullOrWhiteSpace(o.ColeccionAlineaciones);
    }
}
