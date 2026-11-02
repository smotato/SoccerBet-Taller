using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CasaApuestas.Models
{
    [Table("Login")]
    public class Login
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id_login")]
        public int IdLogin { get; set; }

        [Column("id_usuario")]
        public int IdUsuario { get; set; }

        [Column("username")]
        public string Username { get; set; } = null!;

        [Column("password_hash")]
        public string PasswordHash { get; set; } = null!;

        [Column("ultimo_login")]
        public DateTime? UltimoLogin { get; set; }

        [Column("intentos_fallidos")]
        public int IntentosFallidos { get; set; }

        [Column("bloqueado")]
        public bool Bloqueado { get; set; }

        // Propiedad de navegación (opcional, útil para hacer joins fácilmente)
        [ForeignKey("IdUsuario")]
        public Usuario? Usuario { get; set; }
    }
}
