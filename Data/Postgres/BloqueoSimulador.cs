using Npgsql;

namespace CasaApuestas.Data.Postgres
{
    /// <summary>
    /// Garantiza que SOLO UNA instancia de la app simule partidos a la vez (por ejemplo, Visual Studio y una consola abiertas
    /// al mismo tiempo). Usa un candado "advisory" de PostgreSQL: pertenece a una conexion dedicada que se mantiene abierta
    /// mientras esta instancia es la activa. Si la instancia se cae, la conexion se cierra y PostgreSQL libera el candado
    /// solo, asi que otra instancia puede tomar el relevo. El candado es por base de datos (la copia de pruebas no choca con
    /// la real) y no necesita ningun permiso especial.
    /// </summary>
    public sealed class BloqueoSimulador : IAsyncDisposable
    {
        private const long Clave = 7_450_013;     // numero arbitrario, propio de SoccerBet

        private readonly NpgsqlDataSource _fuente;
        private readonly ILogger<BloqueoSimulador> _log;
        private NpgsqlConnection? _conexion;

        public BloqueoSimulador(NpgsqlDataSource fuente, ILogger<BloqueoSimulador> log)
        {
            _fuente = fuente;
            _log = log;
        }

        public bool EsActivo => _conexion is not null;

        /// <summary>true si esta instancia tiene (o acaba de tomar) el candado.</summary>
        public async Task<bool> AdquirirAsync(CancellationToken ct)
        {
            if (_conexion is not null)
            {
                try
                {
                    // Comprobacion de que la conexion que sostiene el candado sigue viva.
                    await using var ping = new NpgsqlCommand("SELECT 1", _conexion);
                    await ping.ExecuteScalarAsync(ct);
                    return true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Se perdio la conexion que sostenia el candado del simulador; se intentara tomarlo de nuevo.");
                    await _conexion.DisposeAsync();
                    _conexion = null;
                }
            }

            var con = await _fuente.OpenConnectionAsync(ct);
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@clave)", con);
                cmd.Parameters.AddWithValue("clave", Clave);
                if ((bool)(await cmd.ExecuteScalarAsync(ct))!)
                {
                    _conexion = con;
                    return true;
                }
                await con.DisposeAsync();
                return false;
            }
            catch
            {
                await con.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_conexion is null) return;
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock_all()", _conexion);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* al cerrar la conexion PostgreSQL libera el candado de todos modos */ }
            await _conexion.DisposeAsync();
            _conexion = null;
        }
    }
}
