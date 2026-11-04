namespace CasaApuestas.Models.Catalogo
{
    /// <summary>Tabla MySQL `estadio`. SOLO LECTURA.</summary>
    public class Estadio
    {
        public int IdEstadio { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public string Pais { get; set; } = string.Empty;
        public int? Capacidad { get; set; }
    }
}
