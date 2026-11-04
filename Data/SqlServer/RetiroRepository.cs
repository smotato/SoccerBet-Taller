using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.SqlServer
{
    /// <summary>
    /// ADO.NET sobre la conexion del DbContext (como AcreditacionRepository y ApuestaRepository): el procedimiento devuelve
    /// parametros OUTPUT, algo que EF no mapea bien.
    /// </summary>
    public class RetiroRepository : IRetiroRepository
    {
        // Errores controlados de sp_RetirarSaldo (ver db/sqlserver/04_cashout_retiros.sql).
        private static readonly HashSet<int> ErroresControlados = new() { 50021, 50027, 50030, 50031 };

        private readonly ApplicationDbContext _context;

        public RetiroRepository(ApplicationDbContext context) => _context = context;

        public async Task<RetiroFila?> BuscarPorClaveAsync(int idCliente, string clave, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText =
                    @"SELECT id_retiro, fecha, importe, medio, estado
                      FROM dbo.Retiro
                      WHERE id_cliente = @id_cliente AND clave = @clave";
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                cmd.Parameters.Add(new SqlParameter("@clave", SqlDbType.NVarChar, 64) { Value = clave });
                await using var r = await cmd.ExecuteReaderAsync(ct);
                return await r.ReadAsync(ct) ? Leer(r) : null;
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        public async Task<IReadOnlyList<RetiroFila>> ListarAsync(int idCliente, int maximo, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText =
                    @"SELECT TOP (@maximo) id_retiro, fecha, importe, medio, estado
                      FROM dbo.Retiro
                      WHERE id_cliente = @id_cliente
                      ORDER BY fecha DESC, id_retiro DESC";
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                cmd.Parameters.Add(new SqlParameter("@maximo", SqlDbType.Int) { Value = maximo });
                var lista = new List<RetiroFila>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) lista.Add(Leer(r));
                return lista;
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        public async Task<ResultadoRetiro> RegistrarAsync(int idCliente, decimal importe, string medio, string clave, decimal minimo, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = "dbo.sp_RetirarSaldo";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                cmd.Parameters.Add(new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = importe });
                cmd.Parameters.Add(new SqlParameter("@medio", SqlDbType.NVarChar, 30) { Value = medio });
                cmd.Parameters.Add(new SqlParameter("@clave", SqlDbType.NVarChar, 64) { Value = clave });
                cmd.Parameters.Add(new SqlParameter("@minimo", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = minimo });
                var idRetiro = new SqlParameter("@id_retiro", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
                var repetido = new SqlParameter("@repetido", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(idRetiro);
                cmd.Parameters.Add(repetido);

                try
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex) when (ErroresControlados.Contains(ex.Number))
                {
                    throw new RetiroRechazadoException(ex.Number, ex.Message);
                }

                // Si faltara una salida (procedimiento desactualizado) se falla en vez de asumir un resultado.
                if (idRetiro.Value is not long id || repetido.Value is not bool rep)
                    throw new InvalidOperationException("sp_RetirarSaldo no devolvio todos sus parametros de salida: la base de datos tiene una version desactualizada del procedimiento.");
                return new ResultadoRetiro(id, rep);
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        private static RetiroFila Leer(IDataRecord r) =>
            new(r.GetInt64(0), r.GetDateTime(1), r.GetDecimal(2), r.GetString(3), r.GetString(4));
    }
}
