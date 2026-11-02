using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CasaApuestas.Models
{
    [Table("Usuario_Estado")]
    public class UsuarioEstado
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id_usuario_estado")]
        public int IdUsuarioEstado { get; set; }

        [Column("id_usuario")]
        public int IdUsuario { get; set; }

        [Column("estado")]
        public string Estado { get; set; } = null!;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }
}
