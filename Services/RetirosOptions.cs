namespace CasaApuestas.Services
{
    /// <summary>
    /// Limites de los retiros simulados (seccion "Retiros" de appsettings.json). El valor NO se escribe en el codigo: si
    /// falta o es invalido, la app no arranca (mensaje claro). El maximo no se configura: es el saldo disponible del cliente.
    /// </summary>
    public class RetirosOptions
    {
        public const string Seccion = "Retiros";

        public decimal ImporteMinimo { get; set; }

        public static bool EsValida(RetirosOptions o) => o.ImporteMinimo > 0;
    }
}
