using Npgsql;

namespace CasaApuestas.Data.Postgres
{
    /// <summary>
    /// ADO.NET con Npgsql (NpgsqlDataSource singleton, con pool). Usa la cuenta soccerbet_app: UPDATE sobre partido/calendario/
    /// cuota y EXECUTE sobre las funciones; no hace falta ningun superusuario.
    /// </summary>
    public class SimuladorRepository : ISimuladorRepository
    {
        private const string Columnas = "id_partido, id_calendario, id_equipo_local, id_equipo_visitante, fecha_hora, minuto, goles_local, goles_visitante";

        private readonly NpgsqlDataSource _fuente;

        public SimuladorRepository(NpgsqlDataSource fuente) => _fuente = fuente;

        public async Task<IReadOnlyList<PartidoSim>> ListarEnVivoAsync(CancellationToken ct = default)
        {
            await using var cmd = _fuente.CreateCommand($"SELECT {Columnas} FROM partido WHERE estado = 'EN_VIVO' ORDER BY id_partido");
            return await LeerPartidosAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<PartidoSim>> ListarProgramadosAsync(int maximo, CancellationToken ct = default)
        {
            await using var cmd = _fuente.CreateCommand($"SELECT {Columnas} FROM partido WHERE estado = 'PROGRAMADO' ORDER BY fecha_hora, id_partido LIMIT @maximo");
            cmd.Parameters.AddWithValue("maximo", maximo);
            return await LeerPartidosAsync(cmd, ct);
        }

        public async Task<bool> IniciarAsync(int idPartido, int idCalendario, DateTime ahora, CancellationToken ct = default)
        {
            // Una transaccion: el partido pasa a EN_VIVO y su jornada a EN_CURSO, o no cambia nada.
            await using var con = await _fuente.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);

            await using (var cmd = new NpgsqlCommand(
                @"UPDATE partido SET estado = 'EN_VIVO', minuto = 0, goles_local = 0, goles_visitante = 0
                  WHERE id_partido = @id AND estado = 'PROGRAMADO' AND fecha_hora <= @ahora", con, tx))
            {
                cmd.Parameters.AddWithValue("id", idPartido);
                cmd.Parameters.AddWithValue("ahora", ahora);
                if (await cmd.ExecuteNonQueryAsync(ct) != 1) return false;     // otro lo inicio, o ya no esta PROGRAMADO
            }

            await using (var cmd = new NpgsqlCommand(
                "UPDATE calendario SET estado = 'EN_CURSO' WHERE id_calendario = @cal AND estado = 'PROGRAMADA'", con, tx))
            {
                cmd.Parameters.AddWithValue("cal", idCalendario);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return true;
        }

        public async Task<bool> AdelantarAsync(int idPartido, DateTime ahora, CancellationToken ct = default)
        {
            await using var cmd = _fuente.CreateCommand(
                "UPDATE partido SET fecha_hora = @ahora WHERE id_partido = @id AND estado = 'PROGRAMADO' AND fecha_hora > @ahora");
            cmd.Parameters.AddWithValue("id", idPartido);
            cmd.Parameters.AddWithValue("ahora", ahora);
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }

        public async Task<bool> FijarMinutoSiNuloAsync(int idPartido, int minuto, CancellationToken ct = default)
        {
            await using var cmd = _fuente.CreateCommand(
                "UPDATE partido SET minuto = @minuto WHERE id_partido = @id AND estado = 'EN_VIVO' AND minuto IS NULL");
            cmd.Parameters.AddWithValue("id", idPartido);
            cmd.Parameters.AddWithValue("minuto", minuto);
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }

        public async Task<bool> ActualizarProgresoAsync(int idPartido, int minuto, int golesLocal, int golesVisitante, CancellationToken ct = default)
        {
            // El minuto nunca retrocede y solo se escribe si el partido sigue EN_VIVO.
            await using var cmd = _fuente.CreateCommand(
                @"UPDATE partido SET minuto = @minuto, goles_local = @gl, goles_visitante = @gv
                  WHERE id_partido = @id AND estado = 'EN_VIVO' AND (minuto IS NULL OR minuto <= @minuto)");
            cmd.Parameters.AddWithValue("id", idPartido);
            cmd.Parameters.AddWithValue("minuto", minuto);
            cmd.Parameters.AddWithValue("gl", golesLocal);
            cmd.Parameters.AddWithValue("gv", golesVisitante);
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }

        public async Task<IReadOnlyList<ResultadoCuotaSim>?> FinalizarAsync(int idPartido, int golesLocal, int golesVisitante, CancellationToken ct = default)
        {
            try
            {
                await using var cmd = _fuente.CreateCommand("SELECT id_cuota, ganadora FROM registrar_resultado_partido(@id, @gl, @gv)");
                cmd.Parameters.AddWithValue("id", idPartido);
                cmd.Parameters.AddWithValue("gl", golesLocal);
                cmd.Parameters.AddWithValue("gv", golesVisitante);
                var lista = new List<ResultadoCuotaSim>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) lista.Add(new ResultadoCuotaSim(r.GetInt32(0), r.GetBoolean(1)));
                return lista;
            }
            // P0001 = RAISE EXCEPTION de la funcion: "El partido no existe o ya fue finalizado". Es el caso normal de una
            // transicion repetida (otra instancia o el modo demostracion ya lo finalizo): no es un error.
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
            {
                return null;
            }
        }

        public async Task<IReadOnlyDictionary<int, Cuotas1X2>> ObtenerCuotasIniciales1X2Async(IReadOnlyCollection<int> idsPartido, CancellationToken ct = default)
        {
            var resultado = new Dictionary<int, Cuotas1X2>();
            if (idsPartido.Count == 0) return resultado;

            // cuota_inicial es la cuota con la que nacio el partido; si fuera NULL se usa la cuota actual.
            await using var cmd = _fuente.CreateCommand(
                @"SELECT id_partido, codigo_seleccion, COALESCE(cuota_inicial, cuota)
                  FROM cuota WHERE id_partido = ANY(@ids) AND codigo_seleccion IN ('LOCAL', 'EMPATE', 'VISITANTE')");
            cmd.Parameters.AddWithValue("ids", idsPartido.ToArray());
            var temporal = new Dictionary<int, (decimal? L, decimal? E, decimal? V)>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    var id = r.GetInt32(0);
                    var (l, e, v) = temporal.GetValueOrDefault(id);
                    var valor = r.GetDecimal(2);
                    switch (r.GetString(1)) { case "LOCAL": l = valor; break; case "EMPATE": e = valor; break; case "VISITANTE": v = valor; break; }
                    temporal[id] = (l, e, v);
                }
            }
            foreach (var (id, t) in temporal) resultado[id] = new Cuotas1X2(t.L, t.E, t.V);
            return resultado;
        }

        private static async Task<IReadOnlyList<PartidoSim>> LeerPartidosAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            var lista = new List<PartidoSim>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                lista.Add(new PartidoSim(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetDateTime(4),
                    r.IsDBNull(5) ? null : r.GetInt32(5), r.IsDBNull(6) ? null : r.GetInt32(6), r.IsDBNull(7) ? null : r.GetInt32(7)));
            }
            return lista;
        }
    }
}
