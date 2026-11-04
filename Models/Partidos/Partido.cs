namespace CasaApuestas.Models.Partidos
{
    /// <summary>
    /// Tabla PostgreSQL `partido`. SOLO LECTURA, sin navegacion.
    /// IdEstadio, IdEquipoLocal e IdEquipoVisitante son IDs logicos hacia MySQL.estadio / MySQL.equipo:
    /// los nombres y escudos se resuelven en la capa de servicios, nunca con un JOIN entre gestores.
    /// FechaHora es la hora LOCAL de Colombia (timestamp sin zona): se compara con la hora de Colombia, no con UTC.
    /// </summary>
    public class Partido
    {
        public int IdPartido { get; set; }
        public int IdCalendario { get; set; }
        public int IdEstadio { get; set; }
        public int IdEquipoLocal { get; set; }
        public int IdEquipoVisitante { get; set; }
        public DateTime FechaHora { get; set; }
        public int? GolesLocal { get; set; }
        public int? GolesVisitante { get; set; }
        public string Estado { get; set; } = string.Empty;   // PROGRAMADO | EN_VIVO | FINALIZADO | SUSPENDIDO
        public DateTime? FechaFinalizacion { get; set; }

        /// <summary>Minuto simulado (0 a 90). NULL en un PROGRAMADO. Lo escribe el simulador.</summary>
        public int? Minuto { get; set; }
    }
}
