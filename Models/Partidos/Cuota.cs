namespace CasaApuestas.Models.Partidos
{
    /// <summary>
    /// Tabla PostgreSQL `cuota`. SOLO LECTURA. Una cuota es una seleccion de un mercado de un partido.
    /// CodigoSeleccion (LOCAL, EMPATE, VISITANTE, MAS_2_5, MENOS_2_5, SI, NO) es el codigo estable que se usa para
    /// reconocer y liquidar; Seleccion es solo el texto para mostrar.
    /// La columna se llama `cuota`, pero una propiedad no puede llamarse igual que su clase: por eso aqui es Valor.
    /// </summary>
    public class Cuota
    {
        public int IdCuota { get; set; }
        public int IdPartido { get; set; }
        public int IdMercado { get; set; }
        public string Seleccion { get; set; } = string.Empty;
        public string CodigoSeleccion { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public bool Disponible { get; set; }
    }
}
