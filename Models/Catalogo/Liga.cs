namespace CasaApuestas.Models.Catalogo
{
    /// <summary>
    /// Tabla MySQL `liga`. SOLO LECTURA: se consulta siempre con AsNoTracking y nunca se guarda desde C#.
    /// Sin propiedades de navegacion: IdEmpresa es un ID logico hacia SQL Server.Empresa (no hay FK entre gestores).
    /// </summary>
    public class Liga
    {
        public int IdLiga { get; set; }
        public int IdEmpresa { get; set; }
        public int IdDeporte { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Pais { get; set; } = string.Empty;
        public string Temporada { get; set; } = string.Empty;
        public bool Activa { get; set; }
        public string? LogoUrl { get; set; }
    }
}
