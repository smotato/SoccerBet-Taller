namespace CasaApuestas.Models.ViewModels
{
    /// <summary>
    /// Una cuota (seleccion) de un mercado en el detalle de partido. Se muestra siempre con su valor; Apostable indica si
    /// se puede pulsar (partido PROGRAMADO y cuota disponible). Los demas datos viajan en atributos data-* del boton para que
    /// el cupon sepa que mostrar; el SERVIDOR vuelve a leer todo en PostgreSQL al confirmar, nunca confia en ellos.
    /// </summary>
    public record SeleccionViewModel(int IdCuota, string Etiqueta, decimal Valor, bool Apostable);

    /// <summary>Un mercado con sus selecciones (2 o 3).</summary>
    public record MercadoViewModel(int IdMercado, string Nombre, IReadOnlyList<SeleccionViewModel> Selecciones);

    /// <summary>
    /// Pagina "Detalle de partido". Estado es el del partido (PROGRAMADO, EN_VIVO, FINALIZADO, SUSPENDIDO).
    /// Error != null: PostgreSQL o MySQL no respondieron.
    /// </summary>
    public class DetallePartidoViewModel
    {
        public int IdPartido { get; init; }
        public string Estado { get; init; } = string.Empty;
        public string Hora { get; init; } = string.Empty;
        public int? Minuto { get; init; }
        public string Competicion { get; init; } = string.Empty;
        public int? Jornada { get; init; }
        public EquipoMiniViewModel Local { get; init; } = new("", "", null, null);
        public EquipoMiniViewModel Visitante { get; init; } = new("", "", null, null);
        public IReadOnlyList<MercadoViewModel> Mercados { get; init; } = Array.Empty<MercadoViewModel>();
        public string? Error { get; init; }

        /// <summary>Eventos y alineaciones (MongoDB). Lo llena el controlador; null si no se pidieron.</summary>
        public EventosPartidoViewModel? Eventos { get; set; }
        public int IdEquipoLocal { get; init; }
        public int IdEquipoVisitante { get; init; }

        public bool EnVivo => Estado == "EN_VIVO";
        public bool Programado => Estado == "PROGRAMADO";
        public bool MostrarMarcador => Estado is "EN_VIVO" or "FINALIZADO";
        public string TextoPartido => $"{Local.Nombre} vs {Visitante.Nombre}";
    }
}
