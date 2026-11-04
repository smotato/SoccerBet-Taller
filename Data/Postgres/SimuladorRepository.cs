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

        public async Task<IReadOnlyList<CuotaPartidoSim>> ObtenerCuotasBaseAsync(int idPartido, CancellationToken ct = default)
        {
            var lista = new List<CuotaPartidoSim>();
            await using var cmd = _fuente.CreateCommand(
                "SELECT id_cuota, codigo_seleccion, COALESCE(cuota_inicial, cuota) FROM cuota WHERE id_partido = @id ORDER BY id_cuota");
            cmd.Parameters.AddWithValue("id", idPartido);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lista.Add(new CuotaPartidoSim(r.GetInt32(0), r.GetString(1), r.GetDecimal(2)));
            return lista;
        }

        public async Task<int> ActualizarCuotasVivoAsync(int idPartido, IReadOnlyCollection<(int IdCuota, decimal Cuota, bool Disponible)> cuotas, CancellationToken ct = default)
        {
            if (cuotas.Count == 0) return 0;
            // Un solo UPDATE ... FROM unnest(...). El EXISTS sobre partido es la guarda de idempotencia: solo si sigue EN_VIVO.
            await using var cmd = _fuente.CreateCommand(
                @"UPDATE cuota c SET cuota = v.cuota, disponible = v.disponible
                  FROM unnest(@ids, @cuotas, @disp) AS v(id, cuota, disponible)
                  WHERE c.id_cuota = v.id AND c.id_partido = @partido
                    AND EXISTS (SELECT 1 FROM partido p WHERE p.id_partido = @partido AND p.estado = 'EN_VIVO')");
            cmd.Parameters.AddWithValue("partido", idPartido);
            cmd.Parameters.AddWithValue("ids", cuotas.Select(c => c.IdCuota).ToArray());
            cmd.Parameters.AddWithValue("cuotas", cuotas.Select(c => c.Cuota).ToArray());
            cmd.Parameters.AddWithValue("disp", cuotas.Select(c => c.Disponible).ToArray());
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<IReadOnlyList<ResultadoCuotaSim>> ObtenerResultadosFinalizadosAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default)
        {
            var lista = new List<ResultadoCuotaSim>();
            if (idsCuota.Count == 0) return lista;
            // fn_cuota_ganadora devuelve NULL si el partido no esta finalizado; aqui solo se piden los FINALIZADO.
            await using var cmd = _fuente.CreateCommand(
                @"SELECT c.id_cuota, COALESCE(fn_cuota_ganadora(c.id_cuota), FALSE)
                  FROM cuota c JOIN partido p ON p.id_partido = c.id_partido
                  WHERE c.id_cuota = ANY(@ids) AND p.estado = 'FINALIZADO'");
            cmd.Parameters.AddWithValue("ids", idsCuota.ToArray());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lista.Add(new ResultadoCuotaSim(r.GetInt32(0), r.GetBoolean(1)));
            return lista;
        }

        public async Task<IReadOnlyList<int>> ObtenerCuotasDePartidosSuspendidosAsync(IReadOnlyCollection<int> idsCuota, CancellationToken ct = default)
        {
            var lista = new List<int>();
            if (idsCuota.Count == 0) return lista;
            await using var cmd = _fuente.CreateCommand(
                @"SELECT c.id_cuota FROM cuota c JOIN partido p ON p.id_partido = c.id_partido
                  WHERE c.id_cuota = ANY(@ids) AND p.estado = 'SUSPENDIDO'");
            cmd.Parameters.AddWithValue("ids", idsCuota.ToArray());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lista.Add(r.GetInt32(0));
            return lista;
        }

        public async Task<int> ContarProgramadosAsync(CancellationToken ct = default)
        {
            await using var cmd = _fuente.CreateCommand("SELECT count(*) FROM partido WHERE estado = 'PROGRAMADO'");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        public async Task<UltimaJornada?> ObtenerUltimaJornadaAsync(CancellationToken ct = default)
        {
            await using var cmd = _fuente.CreateCommand(
                "SELECT id_liga, temporada, numero_jornada FROM calendario ORDER BY id_calendario DESC LIMIT 1");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? new UltimaJornada(r.GetInt32(0), r.GetString(1), r.GetInt32(2)) : null;
        }

        public async Task<IReadOnlyList<MercadoSim>> ListarMercadosAsync(CancellationToken ct = default)
        {
            var lista = new List<MercadoSim>();
            await using var cmd = _fuente.CreateCommand("SELECT id_mercado, nombre FROM mercado WHERE activo ORDER BY id_mercado");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lista.Add(new MercadoSim(r.GetInt32(0), r.GetString(1)));
            return lista;
        }

        public async Task<IReadOnlyList<int>?> CrearJornadaAsync(NuevaJornada j, CancellationToken ct = default)
        {
            await using var con = await _fuente.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);

            int idCalendario;
            await using (var cmd = new NpgsqlCommand(
                @"INSERT INTO calendario (id_liga, numero_jornada, temporada, fecha_inicio, fecha_fin, estado)
                  VALUES (@liga, @num, @temp, @ini, @fin, 'PROGRAMADA')
                  ON CONFLICT (id_liga, temporada, numero_jornada) DO NOTHING
                  RETURNING id_calendario", con, tx))
            {
                cmd.Parameters.AddWithValue("liga", j.IdLiga);
                cmd.Parameters.AddWithValue("num", j.Numero);
                cmd.Parameters.AddWithValue("temp", j.Temporada);
                cmd.Parameters.AddWithValue("ini", DateOnly.FromDateTime(j.FechaInicio));
                cmd.Parameters.AddWithValue("fin", DateOnly.FromDateTime(j.FechaFin));
                var id = await cmd.ExecuteScalarAsync(ct);
                if (id is null) return null;          // esa jornada ya existe: otra instancia la creo primero
                idCalendario = Convert.ToInt32(id);
            }

            var ids = new List<int>();
            foreach (var p in j.Partidos)
            {
                int idPartido;
                await using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO partido (id_calendario, id_estadio, id_equipo_local, id_equipo_visitante, fecha_hora, estado)
                      VALUES (@cal, @est, @loc, @vis, @fecha, 'PROGRAMADO') RETURNING id_partido", con, tx))
                {
                    cmd.Parameters.AddWithValue("cal", idCalendario);
                    cmd.Parameters.AddWithValue("est", p.IdEstadio);
                    cmd.Parameters.AddWithValue("loc", p.IdEquipoLocal);
                    cmd.Parameters.AddWithValue("vis", p.IdEquipoVisitante);
                    cmd.Parameters.AddWithValue("fecha", p.FechaHora);
                    idPartido = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
                }
                ids.Add(idPartido);

                foreach (var c in p.Cuotas)
                {
                    await using var cmd = new NpgsqlCommand(
                        @"INSERT INTO cuota (id_partido, id_mercado, seleccion, codigo_seleccion, cuota, disponible, cuota_inicial)
                          VALUES (@par, @mer, @sel, @cod, @cuota, TRUE, @cuota)", con, tx);
                    cmd.Parameters.AddWithValue("par", idPartido);
                    cmd.Parameters.AddWithValue("mer", c.IdMercado);
                    cmd.Parameters.AddWithValue("sel", c.Seleccion);
                    cmd.Parameters.AddWithValue("cod", c.CodigoSeleccion);
                    cmd.Parameters.AddWithValue("cuota", c.Cuota);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
            return ids;
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
