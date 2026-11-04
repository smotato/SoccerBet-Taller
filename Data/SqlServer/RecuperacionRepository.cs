using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.SqlServer
{
    public record SolicitudCreada(bool Creada, string? Nombre, long IdRecuperacion);

    public interface IRecuperacionRepository
    {
        /// <summary>sp_SolicitarRecuperacion. Creada = false cuando el correo no existe o se alcanzo el limite por hora (no se distingue).</summary>
        Task<SolicitudCreada> SolicitarAsync(string email, string codigoHash, int vigenciaMinutos, string? ip, int maxPorHora, CancellationToken ct = default);

        /// <summary>sp_ObtenerRecuperacion: el hash guardado SOLO si el codigo esta vigente (no usado, no vencido); si no, null.</summary>
        Task<string?> ObtenerHashVigenteAsync(long idRecuperacion, CancellationToken ct = default);

        /// <summary>sp_RestablecerClave: true si cambio la contrasena y consumio el codigo.</summary>
        Task<bool> RestablecerAsync(long idRecuperacion, string codigoHash, string passwordHash, CancellationToken ct = default);
    }

    /// <summary>
    /// ADO.NET sobre la conexion del DbContext (soccerbet_app). La app NO escribe en Recuperacion_Clave: solo ejecuta los
    /// procedimientos de db/sqlserver/06_recuperacion_clave.sql. Aqui solo viaja el HASH del codigo, nunca el codigo.
    /// </summary>
    public class RecuperacionRepository : IRecuperacionRepository
    {
        private readonly ApplicationDbContext _context;
        public RecuperacionRepository(ApplicationDbContext context) => _context = context;

        public async Task<SolicitudCreada> SolicitarAsync(string email, string codigoHash, int vigenciaMinutos, string? ip, int maxPorHora, CancellationToken ct = default)
        {
            return await ConConexionAsync(async con =>
            {
                var creado = new SqlParameter("@creado", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                var nombre = new SqlParameter("@nombre", SqlDbType.NVarChar, 100) { Direction = ParameterDirection.Output };
                await using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "dbo.sp_SolicitarRecuperacion";
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.Add(new SqlParameter("@email", SqlDbType.NVarChar, 150) { Value = email });
                    cmd.Parameters.Add(new SqlParameter("@codigo_hash", SqlDbType.Char, 64) { Value = codigoHash });
                    cmd.Parameters.Add(new SqlParameter("@minutos_vigencia", SqlDbType.Int) { Value = vigenciaMinutos });
                    cmd.Parameters.Add(new SqlParameter("@ip", SqlDbType.VarChar, 45) { Value = (object?)ip ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@max_por_hora", SqlDbType.Int) { Value = maxPorHora });
                    cmd.Parameters.Add(creado);
                    cmd.Parameters.Add(nombre);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                if (creado.Value is not true) return new SolicitudCreada(false, null, 0);

                // El procedimiento no devuelve el id: se lee por el hash (unico, aleatorio de 256 bits). Es solo lectura.
                await using var q = con.CreateCommand();
                q.CommandText = "SELECT id_recuperacion FROM dbo.Recuperacion_Clave WHERE codigo_hash = @h AND usado = 0";
                q.Parameters.Add(new SqlParameter("@h", SqlDbType.Char, 64) { Value = codigoHash });
                var id = await q.ExecuteScalarAsync(ct);
                return id is null or DBNull ? new SolicitudCreada(false, null, 0)
                                            : new SolicitudCreada(true, nombre.Value as string, Convert.ToInt64(id));
            }, ct);
        }

        public async Task<string?> ObtenerHashVigenteAsync(long idRecuperacion, CancellationToken ct = default)
        {
            return await ConConexionAsync(async con =>
            {
                await using var cmd = con.CreateCommand();
                cmd.CommandText = "dbo.sp_ObtenerRecuperacion";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@id_recuperacion", SqlDbType.BigInt) { Value = idRecuperacion });
                var r = await cmd.ExecuteScalarAsync(ct);
                return (r as string)?.Trim();
            }, ct);
        }

        public async Task<bool> RestablecerAsync(long idRecuperacion, string codigoHash, string passwordHash, CancellationToken ct = default)
        {
            return await ConConexionAsync(async con =>
            {
                var resultado = new SqlParameter("@resultado", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                await using var cmd = con.CreateCommand();
                cmd.CommandText = "dbo.sp_RestablecerClave";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@id_recuperacion", SqlDbType.BigInt) { Value = idRecuperacion });
                cmd.Parameters.Add(new SqlParameter("@codigo_hash", SqlDbType.Char, 64) { Value = codigoHash });
                cmd.Parameters.Add(new SqlParameter("@password_hash", SqlDbType.NVarChar, 500) { Value = passwordHash });
                cmd.Parameters.Add(resultado);
                await cmd.ExecuteNonQueryAsync(ct);
                return resultado.Value as string == "OK";
            }, ct);
        }

        private async Task<T> ConConexionAsync<T>(Func<System.Data.Common.DbConnection, Task<T>> accion, CancellationToken ct)
        {
            var con = _context.Database.GetDbConnection();
            var cerrar = con.State != ConnectionState.Open;
            if (cerrar) await con.OpenAsync(ct);
            try { return await accion(con); }
            finally { if (cerrar) await con.CloseAsync(); }
        }
    }
}
