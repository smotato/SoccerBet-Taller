namespace CasaApuestas.Models.Catalogo
{
    /// <summary>
    /// Tabla MySQL `jugador`. SOLO LECTURA. Posicion, NumeroCamiseta y Nacionalidad pueden ser NULL
    /// (p. ej. los jugadores de Fortaleza e Internacional de Bogota); la vista debe tolerarlo.
    /// Posicion, cuando existe, es PORTERO, DEFENSA, MEDIOCAMPISTA o DELANTERO (CHECK en la tabla).
    /// </summary>
    public class Jugador
    {
        public int IdJugador { get; set; }
        public int IdEquipo { get; set; }
        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public int? NumeroCamiseta { get; set; }
        public DateOnly? FechaNacimiento { get; set; }
        public string? Nacionalidad { get; set; }
        public string? Posicion { get; set; }
        public string? FotoUrl { get; set; }
    }
}
