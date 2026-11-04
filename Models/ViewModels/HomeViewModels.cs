namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Equipo tal como lo pinta el home. Goles solo tiene valor en los partidos EN_VIVO.</summary>
    public record EquipoMiniViewModel(string Nombre, string Iniciales, string? EscudoUrl, int? Goles);

    /// <summary>
    /// Una casilla de cuota. Valor es null cuando esa seleccion del 1X2 no esta disponible (se muestra un guion).
    /// IdCuota e IdMercado viajan en atributos data-* para el cupon; el servidor no los toma como verdad al confirmar.
    /// </summary>
    public record CuotaViewModel(string Etiqueta, decimal? Valor, int? IdCuota = null, int? IdMercado = null, bool Disponible = false);

    /// <summary>Una fila de partido. Cuotas son SIEMPRE tres, en orden: local, empate, visitante (mercado 1X2).</summary>
    public record PartidoFilaViewModel(
        int IdPartido,
        bool EnVivo,
        string Hora,
        EquipoMiniViewModel Local,
        EquipoMiniViewModel Visitante,
        IReadOnlyList<CuotaViewModel> Cuotas,
        int MercadosExtra,
        string MercadoNombre,
        int? Minuto = null)
    {
        /// <summary>Solo los partidos PROGRAMADO admiten apuestas en esta fase.</summary>
        public bool Programado => !EnVivo;
        public string TextoPartido => $"{Local.Nombre} vs {Visitante.Nombre}";
    }

    /// <summary>Partidos de una competicion; Titulo es p. ej. "Colombia / Liga BetPlay" y sale de la entidad Liga (MySQL).</summary>
    public record GrupoLigaViewModel(string Titulo, IReadOnlyList<PartidoFilaViewModel> Partidos);

    /// <summary>
    /// Modelo del home. Una seccion sin partidos llega vacia y la vista la oculta.
    /// Error != null significa que PostgreSQL o MySQL no respondieron: la vista muestra ese mensaje en lugar de los partidos.
    /// </summary>
    public class HomeViewModel
    {
        public IReadOnlyList<GrupoLigaViewModel> EnVivo { get; init; } = Array.Empty<GrupoLigaViewModel>();
        public IReadOnlyList<GrupoLigaViewModel> Proximos { get; init; } = Array.Empty<GrupoLigaViewModel>();
        public string? Error { get; init; }
        public bool SinPartidos => EnVivo.Count == 0 && Proximos.Count == 0;
    }
}
