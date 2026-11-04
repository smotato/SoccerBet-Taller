namespace CasaApuestas.Models.ViewModels
{
    /// <summary>
    /// Una seleccion de una apuesta. Cuota es la GUARDADA en la apuesta (no cambia aunque la cuota del partido cambie).
    /// DetalleDisponible = false cuando no se pudo leer su partido/mercado en PostgreSQL (se muestra un texto generico).
    /// </summary>
    public record SeleccionApuestaViewModel(int IdCuota, string Partido, string Mercado, string Seleccion, decimal Cuota, string Estado, bool DetalleDisponible);

    public record ApuestaViewModel(
        long IdApuesta, string Fecha, string Estado, decimal Importe, decimal CuotaTotal, decimal PosibleGanancia,
        IReadOnlyList<SeleccionApuestaViewModel> Selecciones,
        CasaApuestas.Services.ResultadoCashOut? CashOut = null,
        decimal? ValorCobrado = null)
    {
        public bool EsCombinada => Selecciones.Count > 1;
        public string Tipo => EsCombinada ? $"Combinada de {Selecciones.Count}" : "Sencilla";
    }

    public record MovimientoViewModel(string Fecha, string Concepto, string Tipo, decimal Importe, long? IdApuesta)
    {
        public bool EsCredito => Tipo == "CREDITO";
    }

    /// <summary>
    /// Pagina "Mis apuestas". DetalleNoDisponible = true cuando PostgreSQL no respondio: las apuestas (importes y estados,
    /// que viven en SQL Server) se muestran igual, sin los nombres de partidos y mercados. Error != null: SQL Server fallo.
    /// </summary>
    public class MisApuestasViewModel
    {
        public IReadOnlyList<ApuestaViewModel> Apuestas { get; init; } = Array.Empty<ApuestaViewModel>();
        public IReadOnlyList<MovimientoViewModel> Movimientos { get; init; } = Array.Empty<MovimientoViewModel>();
        public bool DetalleNoDisponible { get; init; }
        public string? Error { get; init; }

        public int Pendientes => Apuestas.Count(a => a.Estado == "PENDIENTE");
        public decimal ApostadoPendiente => Apuestas.Where(a => a.Estado == "PENDIENTE").Sum(a => a.Importe);
        public int Ganadas => Apuestas.Count(a => a.Estado == "GANADA");
        public int Perdidas => Apuestas.Count(a => a.Estado == "PERDIDA");
    }
}
