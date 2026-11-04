using System.ComponentModel.DataAnnotations;

namespace CasaApuestas.Models.ViewModels
{
    /// <summary>Formulario "Olvide mi contrasena". Enviado = ya se respondio (siempre con el mismo mensaje).</summary>
    public class OlvideViewModel
    {
        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
        [StringLength(150, ErrorMessage = "El correo no puede superar {1} caracteres.")]
        public string? Email { get; set; }

        public bool Enviado { get; set; }

        /// <summary>SOLO Development y solo si el SMTP no envio. En produccion siempre es null.</summary>
        public string? EnlaceDevelopment { get; set; }
    }

    /// <summary>Formulario de restablecer. Misma politica de contrasena que el registro (RegistroViewModel).</summary>
    public class RestablecerViewModel
    {
        /// <summary>Token del enlace "{id}.{codigo}". Viaja en un campo oculto del formulario, nunca en logs.</summary>
        public string? T { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(8, ErrorMessage = "La contraseña debe tener al menos {1} caracteres.")]
        [StringLength(100, ErrorMessage = "La contraseña no puede superar {1} caracteres.")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Required(ErrorMessage = "Debes confirmar la contraseña.")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        public string? ConfirmarPassword { get; set; }
    }
}
