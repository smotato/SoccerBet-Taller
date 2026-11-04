using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.EntityFrameworkCore;

namespace CasaApuestas.Data.SqlServer
{
    /// <summary>
    /// Se usa ADO.NET sobre la conexion del DbContext (como AcreditacionRepository) porque el procedimiento recibe un
    /// parametro de tipo tabla (dbo.tt_SeleccionApuesta) y devuelve varios parametros OUTPUT, algo que EF no mapea bien.
    /// </summary>
    public class ApuestaRepository : IApuestaRepository
    {
        // Errores controlados de sp_RegistrarApuesta (rango reservado por el script 03_apuestas_combinadas.sql).
        private const int PrimerErrorControlado = 50020;
        private const int UltimoErrorControlado = 50027;

        private readonly ApplicationDbContext _context;

        public ApuestaRepository(ApplicationDbContext context) => _context = context;

        public async Task<ApuestaGuardada?> BuscarPorClaveAsync(int idCliente, string clave, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                // Mismo gestor: el JOIN entre Apuesta y Apuesta_Detalle es valido (la regla es no hacer JOIN ENTRE gestores).
                cmd.CommandText =
                    @"SELECT a.id_apuesta, a.importe, a.cuota_total, a.posible_ganancia, d.id_cuota, d.cuota
                      FROM dbo.Apuesta a
                      JOIN dbo.Apuesta_Detalle d ON d.id_apuesta = a.id_apuesta
                      WHERE a.id_cliente = @id_cliente AND a.clave_idempotencia = @clave
                      ORDER BY d.id_detalle";
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                cmd.Parameters.Add(new SqlParameter("@clave", SqlDbType.NVarChar, 64) { Value = clave });

                long id = 0; decimal importe = 0, total = 0, ganancia = 0;
                var selecciones = new List<(int, decimal)>();
                await using var lector = await cmd.ExecuteReaderAsync(ct);
                while (await lector.ReadAsync(ct))
                {
                    id = lector.GetInt64(0);
                    importe = lector.GetDecimal(1);
                    total = lector.GetDecimal(2);
                    ganancia = lector.GetDecimal(3);
                    selecciones.Add((lector.GetInt32(4), lector.GetDecimal(5)));
                }
                return selecciones.Count == 0 ? null : new ApuestaGuardada(id, importe, total, ganancia, selecciones);
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        public async Task<IReadOnlyList<FilaApuesta>> ListarApuestasDeClienteAsync(int idCliente, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                // Solo las columnas necesarias. id_cliente se usa para FILTRAR (siempre el de la sesion) pero no se devuelve.
                cmd.CommandText =
                    @"SELECT id_apuesta, fecha_apuesta, importe, cuota_total, posible_ganancia, estado_apuesta,
                             id_detalle, id_cuota, cuota, estado_seleccion
                      FROM dbo.vw_DetalleApuestas
                      WHERE id_cliente = @id_cliente
                      ORDER BY fecha_apuesta DESC, id_apuesta DESC, id_detalle";
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });

                var filas = new List<FilaApuesta>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    filas.Add(new FilaApuesta(r.GetInt64(0), r.GetDateTime(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4),
                        r.GetString(5), r.GetInt64(6), r.GetInt32(7), r.GetDecimal(8), r.GetString(9)));
                }
                return filas;
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        public async Task<IReadOnlyList<MovimientoSaldoFila>> ListarMovimientosAsync(int idCliente, int maximo, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                // Mismo gestor: el JOIN Movimiento_Saldo -> Saldo es valido (la regla es no hacer JOIN ENTRE gestores).
                cmd.CommandText =
                    @"SELECT TOP (@maximo) m.id_movimiento, m.fecha, m.tipo, m.importe, m.concepto, m.id_apuesta
                      FROM dbo.Movimiento_Saldo m
                      JOIN dbo.Saldo s ON s.id_saldo = m.id_saldo
                      WHERE s.id_cliente = @id_cliente
                      ORDER BY m.fecha DESC, m.id_movimiento DESC";
                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                cmd.Parameters.Add(new SqlParameter("@maximo", SqlDbType.Int) { Value = maximo });

                var lista = new List<MovimientoSaldoFila>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    lista.Add(new MovimientoSaldoFila(r.GetInt64(0), r.GetDateTime(1), r.GetString(2), r.GetDecimal(3),
                        r.IsDBNull(4) ? "" : r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5)));
                }
                return lista;
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        public async Task<ResultadoRegistro> RegistrarAsync(int idCliente, IReadOnlyList<(int IdCuota, decimal Cuota)> selecciones,
            decimal importe, string clave, decimal premioMaximo, CancellationToken ct = default)
        {
            var conexion = _context.Database.GetDbConnection();
            var cerrar = conexion.State != ConnectionState.Open;
            if (cerrar) await conexion.OpenAsync(ct);
            try
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = "dbo.sp_RegistrarApuesta";
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@id_cliente", SqlDbType.Int) { Value = idCliente });
                // Tipo tabla: se declara la precision exacta de cada columna (cuota DECIMAL(10,4)) para que no se trunque.
                cmd.Parameters.Add(new SqlParameter("@selecciones", SqlDbType.Structured)
                {
                    TypeName = "dbo.tt_SeleccionApuesta",
                    Value = ComoRegistros(selecciones)
                });
                cmd.Parameters.Add(new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = importe });
                cmd.Parameters.Add(new SqlParameter("@clave", SqlDbType.NVarChar, 64) { Value = clave });
                cmd.Parameters.Add(new SqlParameter("@premio_maximo", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = premioMaximo });

                var idApuesta = new SqlParameter("@id_apuesta", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
                var cuotaTotal = new SqlParameter("@cuota_total", SqlDbType.Decimal) { Precision = 18, Scale = 4, Direction = ParameterDirection.Output };
                var ganancia = new SqlParameter("@posible_ganancia", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };
                var repetida = new SqlParameter("@repetida", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(idApuesta);
                cmd.Parameters.Add(cuotaTotal);
                cmd.Parameters.Add(ganancia);
                cmd.Parameters.Add(repetida);

                try
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex) when (ex.Number is >= PrimerErrorControlado and <= UltimoErrorControlado)
                {
                    throw new ApuestaRechazadaException(ex.Number, ex.Message);
                }

                // Si faltara alguna salida (procedimiento desactualizado) se falla en vez de asumir un resultado.
                if (idApuesta.Value is not long id || cuotaTotal.Value is not decimal total
                    || ganancia.Value is not decimal premio || repetida.Value is not bool rep)
                    throw new InvalidOperationException("sp_RegistrarApuesta no devolvio todos sus parametros de salida: la base de datos tiene una version desactualizada del procedimiento.");

                return new ResultadoRegistro(id, total, premio, rep);
            }
            finally
            {
                if (cerrar) await conexion.CloseAsync();
            }
        }

        private static IEnumerable<SqlDataRecord> ComoRegistros(IReadOnlyList<(int IdCuota, decimal Cuota)> selecciones)
        {
            var meta = new[]
            {
                new SqlMetaData("id_cuota", SqlDbType.Int),
                new SqlMetaData("cuota", SqlDbType.Decimal, 10, 4)
            };
            foreach (var (idCuota, cuota) in selecciones)
            {
                var r = new SqlDataRecord(meta);
                r.SetInt32(0, idCuota);
                r.SetDecimal(1, cuota);
                yield return r;
            }
        }
    }
}
