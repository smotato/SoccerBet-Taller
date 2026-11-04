namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Tarjeta de un equipo en la lista. Iniciales se usa como escudo generico cuando EscudoUrl es NULL.</summary>
    public record EquipoCardViewModel(int IdEquipo, string Nombre, string Iniciales, string? EscudoUrl, string? Ciudad, string? EstadioNombre);

    /// <summary>
    /// Pagina "Equipos". Error != null significa que el catalogo (MySQL) no respondio; la vista muestra ese mensaje.
    /// </summary>
    public class EquiposListaViewModel
    {
        public string? LigaNombre { get; init; }
        public string? Temporada { get; init; }
        public string? Pais { get; init; }
        public IReadOnlyList<EquipoCardViewModel> Equipos { get; init; } = Array.Empty<EquipoCardViewModel>();
        public string? Error { get; init; }
    }

    /// <summary>Una fila de la plantilla. Numero y Nacionalidad pueden ser NULL (la vista muestra "–" o vacio).</summary>
    public record JugadorFilaViewModel(int? Numero, string Nombre, string? Nacionalidad);

    /// <summary>
    /// Bloque de la plantilla. Titulo es "Porteros", "Defensas", etc.; para los jugadores sin posicion es "Plantilla"
    /// y EsSinPosicion = true (van al final).
    /// </summary>
    public record GrupoPlantillaViewModel(string Titulo, bool EsSinPosicion, IReadOnlyList<JugadorFilaViewModel> Jugadores);

    /// <summary>Pagina de detalle de un equipo: datos, estadio y plantilla agrupada.</summary>
    public class EquipoDetalleViewModel
    {
        public int IdEquipo { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string Iniciales { get; init; } = string.Empty;
        public string? EscudoUrl { get; init; }
        public string? Ciudad { get; init; }
        public string? Pais { get; init; }
        public string? EstadioNombre { get; init; }
        public int? EstadioCapacidad { get; init; }
        public string? LigaNombre { get; init; }
        public string? Temporada { get; init; }
        public IReadOnlyList<GrupoPlantillaViewModel> Grupos { get; init; } = Array.Empty<GrupoPlantillaViewModel>();
        public int TotalJugadores => Grupos.Sum(g => g.Jugadores.Count);
        public string? Error { get; init; }
    }
}
