namespace CasaApuestas.Services
{
    /// <summary>
    /// Cada cuantos segundos las paginas se refrescan solas (seccion "Vivo" de appsettings.json). UNA sola configuracion para todo:
    /// la usan vivo.js (minuto, marcador y cuotas del home y del detalle) y el cash out de Mis apuestas. _Layout la entrega a los
    /// scripts en <body data-vivo-intervalo="...">; ningun script tiene un numero escrito aparte.
    /// </summary>
    public class VivoOptions
    {
        public const string Seccion = "Vivo";

        public int IntervaloActualizacionSegundos { get; set; }

        public static bool EsValida(VivoOptions o) => o.IntervaloActualizacionSegundos is >= 1 and <= 300;
    }
}
