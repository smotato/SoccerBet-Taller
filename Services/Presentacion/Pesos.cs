using System.Globalization;

namespace CasaApuestas.Services.Presentacion
{
    /// <summary>Formato de pesos colombianos para pantallas y mensajes: "$ 120.000" (sin decimales si es entero).</summary>
    public static class Pesos
    {
        // Punto para miles y coma para decimales, definido a mano para no depender de la cultura instalada en el servidor.
        private static readonly NumberFormatInfo FormatoCo = new()
        {
            NumberGroupSeparator = ".",
            NumberDecimalSeparator = ",",
            NumberGroupSizes = new[] { 3 }
        };

        public static string Formatear(decimal valor)
        {
            var formato = valor == decimal.Truncate(valor) ? "N0" : "N2";
            return "$ " + valor.ToString(formato, FormatoCo);
        }
    }
}
