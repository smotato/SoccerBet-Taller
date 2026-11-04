namespace CasaApuestas.Models.Partidos
{
    /// <summary>Tabla PostgreSQL `mercado` (1X2, total de goles 2.5, ambos anotan). SOLO LECTURA.</summary>
    public class Mercado
    {
        public int IdMercado { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public bool Activo { get; set; }
    }
}
