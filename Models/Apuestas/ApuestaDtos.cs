namespace CasaApuestas.Models.Apuestas
{
    /// <summary>Una seleccion tal como la envia el navegador: SOLO el id de la cuota y la cuota que el cliente vio.</summary>
    public class SeleccionRequest
    {
        public int IdCuota { get; set; }
        public decimal Cuota { get; set; }
    }

    /// <summary>
    /// Lo unico que el navegador envia al confirmar un cupon. Nada de esto se toma como verdad: el servidor relee cada cuota,
    /// partido y mercado en PostgreSQL. Clave = GUID generado al abrir el cupon (idempotencia).
    /// </summary>
    public class ConfirmarApuestaRequest
    {
        public string? Clave { get; set; }
        public decimal Importe { get; set; }
        public List<SeleccionRequest>? Selecciones { get; set; }
    }

    /// <summary>Una seleccion que fallo y por que. Permite al cupon senalarla y ofrecer quitarla.</summary>
    public record ErrorSeleccion(int? IdCuota, int? IdPartido, int? IdMercado, string Mensaje);

    /// <summary>Una cuota cuyo valor cambio desde que el cliente la vio.</summary>
    public record CambioCuota(int IdCuota, string Partido, string Mercado, string Seleccion, decimal Anterior, decimal Actual);

    /// <summary>Una seleccion de una apuesta registrada, con textos para mostrar. Cuota = la guardada en la apuesta.</summary>
    public record SeleccionRegistrada(int IdCuota, string Partido, string Mercado, string Seleccion, decimal Cuota);

    public record ApuestaRegistrada(
        long IdApuesta, decimal CuotaTotal, decimal Importe, decimal PosibleGanancia, bool Repetida,
        IReadOnlyList<SeleccionRegistrada> Selecciones);

    /// <summary>
    /// Respuesta de Confirmar. Codigo: OK, REPETIDA (la clave ya existia: no se cobro de nuevo), INVALIDA, CUOTAS_CAMBIADAS,
    /// SALDO, RECHAZADA (la rechazo el procedimiento almacenado) o NO_DISPONIBLE (no se pudo validar contra PostgreSQL).
    /// Status es el codigo HTTP con que el controlador responde.
    /// </summary>
    public class ConfirmarApuestaResultado
    {
        public bool Exito { get; init; }
        public string Codigo { get; init; } = string.Empty;
        public string Mensaje { get; init; } = string.Empty;
        public IReadOnlyList<ErrorSeleccion> Errores { get; init; } = Array.Empty<ErrorSeleccion>();
        public IReadOnlyList<CambioCuota> Cambios { get; init; } = Array.Empty<CambioCuota>();
        /// <summary>Todas las cuotas vigentes del cupon (idCuota -> cuota actual), para que el cupon se actualice.</summary>
        public IReadOnlyList<SeleccionRequest> CuotasActuales { get; init; } = Array.Empty<SeleccionRequest>();
        public ApuestaRegistrada? Apuesta { get; init; }
        public decimal? Saldo { get; init; }
        public string? SaldoTexto { get; init; }
        public int Status { get; init; }
    }
}
