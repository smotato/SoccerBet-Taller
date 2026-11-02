using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CasaApuestas.Models
{
    /// <summary>
    /// Tabla Saldo (SQL Server). SOLO LECTURA desde C#: el dinero se mueve unicamente con los procedimientos
    /// (sp_RegistrarApuesta, sp_AcreditarSaldo), dentro de transaccion y con registro en Movimiento_Saldo.
    /// Esta clase sirve para mostrar el saldo; nunca se inserta ni se actualiza. Siempre con AsNoTracking().
    /// </summary>
    [Table("Saldo")]
    public class Saldo
    {
        [Key]
        [Column("id_saldo")]
        public long IdSaldo { get; set; }

        [Column("id_cliente")]
        public int IdCliente { get; set; }

        [Column("saldo_actual", TypeName = "decimal(18,2)")]
        public decimal SaldoActual { get; set; }

        [Column("fecha_actualizacion")]
        public DateTime FechaActualizacion { get; set; }
    }
}
