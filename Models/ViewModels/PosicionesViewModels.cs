namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Una fila de la tabla de posiciones. Clasifica = true en las primeras posiciones (cuadrangulares).</summary>
    public record FilaPosicionViewModel(
        int Posicion, int IdEquipo, string Nombre, string Iniciales, string? EscudoUrl,
        int Jugados, int Ganados, int Empatados, int Perdidos,
        int GolesFavor, int GolesContra, int Diferencia, int Puntos, bool Clasifica);

    /// <summary>Pagina "Tabla de posiciones". Error != null: PostgreSQL o MySQL no respondieron.</summary>
    public class PosicionesViewModel
    {
        public string? LigaNombre { get; init; }
        public string? Pais { get; init; }
        public string? Temporada { get; init; }
        public int Cuadrangulares { get; init; }
        public IReadOnlyList<FilaPosicionViewModel> Filas { get; init; } = Array.Empty<FilaPosicionViewModel>();
        public string? Error { get; init; }
    }
}
