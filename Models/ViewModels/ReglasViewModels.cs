namespace CasaApuestas.Models.ViewModels
{
    public record ReglaViewModel(int Orden, string Nombre, string Descripcion);

    /// <summary>Una seccion de la pagina. Id es el ancla (#futbol, #apuestas) a la que enlaza, por ejemplo, el cupon.</summary>
    public record SeccionReglasViewModel(string Id, string Titulo, IReadOnlyList<ReglaViewModel> Reglas);

    /// <summary>Pagina "Reglas". Error != null: MongoDB no respondio.</summary>
    public class ReglasViewModel
    {
        public SeccionReglasViewModel Futbol { get; init; } = new("futbol", "Reglas del fútbol", Array.Empty<ReglaViewModel>());
        public SeccionReglasViewModel Apuestas { get; init; } = new("apuestas", "Reglas de apuestas", Array.Empty<ReglaViewModel>());
        public string? Error { get; init; }

        public int Total => Futbol.Reglas.Count + Apuestas.Reglas.Count;
    }
}
