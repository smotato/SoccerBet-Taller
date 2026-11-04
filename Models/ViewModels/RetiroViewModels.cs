namespace CasaApuestas.Models.ViewModels
{
    public record RetiroItemViewModel(long IdRetiro, string Fecha, string Medio, string Estado, string ImporteTexto);

    /// <summary>
    /// Pagina "Retirar saldo". Clave es el GUID de idempotencia del formulario (uno por formulario abierto).
    /// Mensaje = exito (viene de TempData tras retirar); Error = motivo por el que no se pudo retirar.
    /// </summary>
    public class RetirarViewModel
    {
        public decimal SaldoDisponible { get; init; }
        public string SaldoTexto { get; init; } = "";
        public decimal Minimo { get; init; }
        public string MinimoTexto { get; init; } = "";
        public string Clave { get; init; } = "";
        public IReadOnlyList<string> Medios { get; init; } = Array.Empty<string>();
        public IReadOnlyList<RetiroItemViewModel> Retiros { get; init; } = Array.Empty<RetiroItemViewModel>();
        public string? ImporteIngresado { get; init; }
        public string? MedioSeleccionado { get; init; }
        public string? Mensaje { get; init; }
        public string? Error { get; init; }

        /// <summary>Solo se puede retirar si el saldo alcanza el minimo.</summary>
        public bool PuedeRetirar => SaldoDisponible >= Minimo;
        /// <summary>Tope para el atributo max del campo (pesos enteros).</summary>
        public long MaximoEntero => (long)decimal.Truncate(SaldoDisponible);
    }
}
