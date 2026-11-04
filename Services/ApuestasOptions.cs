namespace CasaApuestas.Services
{
    /// <summary>
    /// Limites de las apuestas (seccion "Apuestas" de appsettings.json). Los valores NO se escriben en el codigo:
    /// si falta alguno o es invalido, la app no arranca (mensaje claro) en lugar de apostar con limites inventados.
    /// </summary>
    public class ApuestasOptions
    {
        public const string Seccion = "Apuestas";

        public decimal ImporteMinimo { get; set; }
        public decimal ImporteMaximo { get; set; }
        public int MaxSelecciones { get; set; }
        public decimal PremioMaximo { get; set; }

        public static bool EsValida(ApuestasOptions o) =>
            o.ImporteMinimo > 0 && o.ImporteMaximo >= o.ImporteMinimo && o.MaxSelecciones >= 1 && o.PremioMaximo > 0;
    }
}
