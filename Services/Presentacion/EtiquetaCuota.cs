using CasaApuestas.Models.Partidos;

namespace CasaApuestas.Services.Presentacion
{
    public static class EtiquetaCuota
    {
        /// <summary>1X2 se rotula con los nombres de los equipos (como el home); los demas mercados con el texto de la cuota.</summary>
        public static string Texto(Cuota c, string local, string visitante) => c.CodigoSeleccion switch
        {
            "LOCAL" => local,
            "VISITANTE" => visitante,
            "EMPATE" => "Empate",
            _ => c.Seleccion
        };
    }
}
