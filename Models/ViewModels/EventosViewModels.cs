namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Una fila de la cronologia. Tipo: GOL, AMARILLA, ROJA, INICIO o FIN (decide el icono). Todo el texto se codifica en la vista.</summary>
    public record EventoFilaViewModel(string Minuto, string Tipo, string Texto);

    public record JugadorAlineacionViewModel(int? Numero, string Nombre, string? Posicion);

    public record AlineacionEquipoViewModel(string Equipo, string? Formacion, IReadOnlyList<JugadorAlineacionViewModel> Titulares);

    /// <summary>
    /// Eventos y alineaciones de un partido (MongoDB). Los NOMBRES salen del catalogo de MySQL, nunca del texto guardado en Mongo.
    /// MongoNoDisponible: Mongo no respondio; el resto de la pagina sigue funcionando y la vista muestra un aviso amable.
    /// MensajeEstado: texto cuando no hay lista que mostrar ("Aun no hay eventos", "Partido anulado"...).
    /// </summary>
    public class EventosPartidoViewModel
    {
        public int IdPartido { get; init; }
        public string Estado { get; init; } = string.Empty;
        public bool MongoNoDisponible { get; init; }
        public string? MensajeEstado { get; init; }
        public IReadOnlyList<EventoFilaViewModel> Eventos { get; init; } = Array.Empty<EventoFilaViewModel>();
        public IReadOnlyList<AlineacionEquipoViewModel> Alineaciones { get; init; } = Array.Empty<AlineacionEquipoViewModel>();
        public bool EnVivo => Estado == "EN_VIVO";
    }
}
