namespace CasaApuestas.Models.Catalogo
{
    /// <summary>Tabla MySQL `equipo`. SOLO LECTURA, sin navegacion (IdLiga se resuelve en el servicio).</summary>
    public class Equipo
    {
        public int IdEquipo { get; set; }
        public int IdLiga { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Ciudad { get; set; }
        public bool Activo { get; set; }
        public string? EscudoUrl { get; set; }
    }
}
