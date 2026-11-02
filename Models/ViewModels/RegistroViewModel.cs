using System.ComponentModel.DataAnnotations;

namespace CasaApuestas.Models.ViewModels
{
    /// <summary>
    /// Datos del formulario de registro de cliente (RF2).
    /// Las validaciones de DataAnnotations corren en el servidor (ModelState); el procedimiento
    /// sp_RegistrarCliente vuelve a validar la edad y las duplicidades dentro de SQL Server.
    /// </summary>
    public class RegistroViewModel
    {
        [Required(ErrorMessage = "Los nombres son obligatorios.")]
        [StringLength(100, ErrorMessage = "Los nombres no pueden superar {1} caracteres.")]
        public string Nombres { get; set; } = null!;

        [Required(ErrorMessage = "Los apellidos son obligatorios.")]
        [StringLength(100, ErrorMessage = "Los apellidos no pueden superar {1} caracteres.")]
        public string Apellidos { get; set; } = null!;

        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
        [StringLength(150, ErrorMessage = "El correo no puede superar {1} caracteres.")]
        public string Email { get; set; } = null!;

        // Opcional: sin [Required]. El procedimiento lo recibe como NULL cuando viene vacío.
        [StringLength(30, ErrorMessage = "El teléfono no puede superar {1} caracteres.")]
        public string? Telefono { get; set; }

        [Required(ErrorMessage = "El documento es obligatorio.")]
        [StringLength(50, ErrorMessage = "El documento no puede superar {1} caracteres.")]
        public string Documento { get; set; } = null!;

        // Nullable para que "campo vacío" produzca el mensaje de [Required] y no un error de conversion.
        [Required(ErrorMessage = "La fecha de nacimiento es obligatoria.")]
        [MayorDeEdad(18, ErrorMessage = "Debes ser mayor de 18 años para registrarte.")]
        public DateTime? FechaNacimiento { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(8, ErrorMessage = "La contraseña debe tener al menos {1} caracteres.")]
        [StringLength(100, ErrorMessage = "La contraseña no puede superar {1} caracteres.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "Debes confirmar la contraseña.")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        public string ConfirmarPassword { get; set; } = null!;
    }

    /// <summary>Valida que la fecha corresponda a una persona con al menos <c>edadMinima</c> años cumplidos.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MayorDeEdadAttribute : ValidationAttribute
    {
        private readonly int _edadMinima;

        public MayorDeEdadAttribute(int edadMinima) => _edadMinima = edadMinima;

        public override bool IsValid(object? value)
        {
            // Si viene vacio lo reporta [Required]; aqui no se duplica el error.
            if (value is not DateTime nacimiento) return true;

            // Cumple la edad si su cumpleaños numero _edadMinima ya ocurrio (hoy incluido).
            return nacimiento.Date <= DateTime.Today.AddYears(-_edadMinima);
        }
    }
}
