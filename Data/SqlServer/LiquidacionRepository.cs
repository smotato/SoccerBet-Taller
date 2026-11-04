using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

namespace CasaApuestas.Data.SqlServer
{
    /// <summary>
    /// Singleton para el servicio en segundo plano: abre su propia conexion SQL por llamada (el DbContext de la app es scoped y
    /// no sirve en un servicio de larga vida). Usa la misma cuenta soccerbet_app (EXECUTE sobre dbo + los tipos de tabla);
    /// no hace falta ningun permiso adicional.
    /// </summary>
    public class LiquidacionRepository : ILiquidacionRepository
    {
        private readonly string _cadena;

        public LiquidacionRepository(IConfiguration config)
        {
            _cadena = config.GetConnectionString("DefaultConnection") ?? "";
        }

        public async Task<IReadOnlyList<int>> ListarCuotasPendientesAsync(CancellationToken ct = default)
        {
            await using var con = await AbrirAsync(ct);
            await using var cmd = con.CreateCommand();
            cmd.CommandText =
                @"SELECT DISTINCT d.id_cuota
                  FROM dbo.Apuesta_Detalle d
                  JOIN dbo.Apuesta a ON a.id_apuesta = d.id_apuesta
                  WHERE a.estado = 'PENDIENTE' AND d.estado = 'PENDIENTE'";
            var lista = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lista.Add(r.GetInt32(0));
            return lista;
        }

        public async Task<ResumenLiquidacion> ResolverAsync(IReadOnlyCollection<(int IdCuota, bool Ganadora)> resultados, CancellationToken ct = default)
        {
            if (resultados.Count == 0) return new ResumenLiquidacion(0, 0, 0);

            await using var con = await AbrirAsync(ct);
            await using var cmd = con.CreateCommand();
            cmd.CommandText = "dbo.sp_ResolverApuestasPartido";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@resultados", SqlDbType.Structured)
            {
                TypeName = "dbo.tt_ResultadoCuota",
                Value = Registros(resultados)
            });
            return await LeerResumenAsync(cmd, ct);
        }

        public async Task<ResumenLiquidacion> AnularAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default)
        {
            if (idsCuota.Count == 0) return new ResumenLiquidacion(0, 0, 0);

            await using var con = await AbrirAsync(ct);
            await using var cmd = con.CreateCommand();
            cmd.CommandText = "dbo.sp_AnularApuestasPartido";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@cuotas", SqlDbType.Structured)
            {
                TypeName = "dbo.tt_CuotaAnulada",
                Value = Registros(idsCuota)
            });
            return await LeerResumenAsync(cmd, ct);
        }

        private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_cadena))
                throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection (configurala con user-secrets).");
            var con = new SqlConnection(_cadena);
            await con.OpenAsync(ct);
            return con;
        }

        // sp_LiquidarApuestas termina con un SELECT (ganadas, perdidas, anuladas) que llega como primer resultado.
        private static async Task<ResumenLiquidacion> LeerResumenAsync(SqlCommand cmd, CancellationToken ct)
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct)
                ? new ResumenLiquidacion(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2))
                : new ResumenLiquidacion(0, 0, 0);
        }

        private static IEnumerable<SqlDataRecord> Registros(IEnumerable<(int IdCuota, bool Ganadora)> resultados)
        {
            var meta = new[] { new SqlMetaData("id_cuota", SqlDbType.Int), new SqlMetaData("ganadora", SqlDbType.Bit) };
            foreach (var (id, ganadora) in resultados)
            {
                var r = new SqlDataRecord(meta);
                r.SetInt32(0, id);
                r.SetBoolean(1, ganadora);
                yield return r;
            }
        }

        private static IEnumerable<SqlDataRecord> Registros(IEnumerable<int> ids)
        {
            var meta = new[] { new SqlMetaData("id_cuota", SqlDbType.Int) };
            foreach (var id in ids.Distinct())
            {
                var r = new SqlDataRecord(meta);
                r.SetInt32(0, id);
                yield return r;
            }
        }
    }
}
