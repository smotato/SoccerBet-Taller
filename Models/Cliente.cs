using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CasaApuestas.Models
{
    /// <summary>
    /// Tabla Cliente (SQL Server). SOLO LECTURA desde C#: los clientes se crean unicamente con
    /// sp_RegistrarCliente y el estado lo cambian los procedimientos; esta clase nunca se inserta ni se actualiza.
    /// Siempre se consulta con AsNoTracking().
    /// </summary>
    [Table("Cliente")]
    public class Cliente
    {
        [Key]
        [Column("id_cliente")]
        public int IdCliente { get; set; }

        [Column("id_usuario")]
        public int IdUsuario { get; set; }

        [Column("documento")]
        public string Documento { get; set; } = null!;

        [Column("fecha_nacimiento", TypeName = "date")]
        public DateTime FechaNacimiento { get; set; }

        // Valores usados en el alcance actual: 'ACTIVO' (puede iniciar sesion y apostar) u otro estado.
        [Column("estado")]
        public string Estado { get; set; } = null!;

        [Column("fecha_registro")]
        public DateTime FechaRegistro { get; set; }
    }
}
