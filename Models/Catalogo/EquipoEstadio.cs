namespace CasaApuestas.Models.Catalogo
{
    /// <summary>Tabla MySQL `equipo_estadio` (relacion N:M entre equipo y estadio). SOLO LECTURA.</summary>
    public class EquipoEstadio
    {
        public int IdEquipoEstadio { get; set; }
        public int IdEquipo { get; set; }
        public int IdEstadio { get; set; }
        public bool EsPrincipal { get; set; }
    }
}
