namespace CasaApuestas.Models.Partidos
{
    /// <summary>
    /// Tabla PostgreSQL `calendario` (jornadas). SOLO LECTURA, sin navegacion.
    /// IdLiga es un ID logico hacia MySQL.liga (no hay llave foranea entre gestores).
    /// </summary>
    public class Calendario
    {
        public int IdCalendario { get; set; }
        public int IdLiga { get; set; }
        public int NumeroJornada { get; set; }
        public string Temporada { get; set; } = string.Empty;
        public DateOnly? FechaInicio { get; set; }
        public DateOnly? FechaFin { get; set; }
        public string Estado { get; set; } = string.Empty;   // PROGRAMADA | EN_CURSO | FINALIZADA
    }
}
