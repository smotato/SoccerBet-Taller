using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CasaApuestas.Models
{
    [Table("Usuario")]
    public class Usuario
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id_usuario")]
        public int IdUsuario { get; set; }

        [Column("nombres")]
        public string Nombres { get; set; } = null!;

        [Column("apellidos")]
        public string Apellidos { get; set; } = null!;

        [Column("email")]
        public string Email { get; set; } = null!;

        [Column("telefono")]
        public string? Telefono { get; set; }
    }
}
