namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Datos de la zona derecha de la cabecera (saldo, avatar, entrar/salir).</summary>
    public class WalletViewModel
    {
        // true = hay una sesion de cliente valida; false = visitante.
        public bool EsCliente { get; set; }

        public string Nombre { get; set; } = "";
        public string Iniciales { get; set; } = "?";

        // Saldo ya formateado ("$ 120.000"), o "—" si no se pudo consultar.
        public string SaldoTexto { get; set; } = "";
    }
}
