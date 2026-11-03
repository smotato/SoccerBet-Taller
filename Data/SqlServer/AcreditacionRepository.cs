using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.SqlServer
{
    /// <summary>Acredita dinero al saldo de un cliente. Es la UNICA via por la que una recarga toca el saldo.</summary>
    public interface IAcreditacionRepository
    {
        /// <summary>
        /// Ejecuta dbo.sp_AcreditarSaldo. Devuelve true si el procedimiento dijo "ya acreditado" (el concepto ya existia:
        /// no se sumo nada), false si acredito ahora.
        /// </summary>
        Task<bool> AcreditarAsync(int idCliente, decimal importe, string concepto, CancellationToken ct = default);
    }

    /// <summary>
    /// La app NO escribe directo en Saldo ni Movimiento_Saldo (el login soccerbet_app solo tiene EXECUTE): el dinero
    /// cambia unicamente a traves del procedimiento almacenado, que corre dentro de una transaccion en SQL Server.
    /// Se usa ADO.NET sobre la conexion del DbContext porque el SP devuelve un resultado (ya_acreditado) y EF no mapea
    /// bien un escalar de un procedimiento.
    /// </summary>
    public class AcreditacionRepository : IAcreditacionRepository
    {
        private readonly ApplicationDbContext _context;

        public AcreditacionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> AcreditarAsync(int idCliente, decimal importe, string concepto, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrarAlTerminar = conexion.State != ConnectionState.Open;
            if (cerrarAlTerminar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = "dbo.sp_AcreditarSaldo";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                cmd.Parameters.Add(new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = importe });
                cmd.Parameters.Add(new SqlParameter("@concepto", SqlDbType.NVarChar, 250) { Value = concepto });

                var resultado = await cmd.ExecuteScalarAsync(ct);
                // Si el SP aun no tiene la version idempotente (no devuelve fila), se asume "acredito ahora".
                return resultado is bool yaAcreditado && yaAcreditado;
            }
            finally
            {
                if (cerrarAlTerminar) await conexion.CloseAsync();
            }
        }
    }
}
