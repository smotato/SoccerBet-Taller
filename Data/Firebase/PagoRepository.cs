using Google.Cloud.Firestore;

namespace CasaApuestas.Data.Firebase
{
    /// <summary>
    /// Implementacion sobre Firestore, coleccion "pagos".
    /// ID del documento = "wompi_{referencia}": la referencia la genera SoccerBet ANTES de pagar, asi que el documento
    /// se puede crear de inmediato (el id de transaccion de Wompi aun no existe). Como el ID es determinista, un
    /// segundo intento de crear el mismo pago falla, y un evento repetido solo actualiza el mismo documento.
    /// Los documentos se leen y escriben como diccionarios: los nombres de campo quedan visibles y coinciden con docs/firestore.md.
    /// </summary>
    public class PagoRepository : IPagoRepository
    {
        private const string Coleccion = "pagos";
        private const string Proveedor = "wompi";

        private readonly FirestoreDb _db;

        public PagoRepository(FirestoreDb db)
        {
            _db = db;
        }

        private DocumentReference Doc(string referencia) =>
            _db.Collection(Coleccion).Document($"{Proveedor}_{referencia}");

        private static Timestamp Ahora() => Timestamp.FromDateTime(DateTime.UtcNow);

        public async Task CrearPendienteAsync(long idCliente, string referencia, long importePesos, CancellationToken ct = default)
        {
            var ahora = Ahora();
            var datos = new Dictionary<string, object>
            {
                ["id_cliente"] = idCliente,
                ["proveedor"] = Proveedor,
                ["id_transaccion"] = "",
                ["referencia"] = referencia,
                ["importe"] = importePesos,
                ["tipo"] = "DEPOSITO",
                ["metodo"] = "",
                ["estado"] = "PENDIENTE",
                ["acreditado"] = false,
                ["payload"] = new Dictionary<string, object>(),
                ["fecha"] = ahora,
                ["fecha_actualizacion"] = ahora
            };
            // CreateAsync falla si el documento ya existe: nunca se pisa un pago.
            await Doc(referencia).CreateAsync(datos, ct);
        }

        public async Task<PagoRecarga?> ObtenerAsync(string referencia, CancellationToken ct = default)
        {
            var snap = await Doc(referencia).GetSnapshotAsync(ct);
            return snap.Exists ? Mapear(snap) : null;
        }

        public async Task<IReadOnlyList<PagoRecarga>> ListarPendientesAsync(long idCliente, CancellationToken ct = default)
        {
            // Dos igualdades no necesitan indice compuesto. El orden y el filtro fino se hacen en memoria
            // (un cliente tiene muy pocos pagos sin acreditar).
            var consulta = await _db.Collection(Coleccion)
                .WhereEqualTo("id_cliente", idCliente)
                .WhereEqualTo("acreditado", false)
                .GetSnapshotAsync(ct);

            return consulta.Documents
                .Select(Mapear)
                .Where(p => p.Estado == "PENDIENTE" || p.Estado == "APROBADO")
                .OrderByDescending(p => p.Fecha)
                .ToList();
        }

        public Task ActualizarDesdeWompiAsync(string referencia, string idTransaccion, string estado, string metodo,
                                              IDictionary<string, object?> payload, CancellationToken ct = default)
        {
            var doc = Doc(referencia);
            return _db.RunTransactionAsync(async tx =>
            {
                var snap = await tx.GetSnapshotAsync(doc, ct);
                if (!snap.Exists) throw new InvalidOperationException("El pago no existe.");

                // Un pago APROBADO es definitivo: una consulta posterior no lo devuelve a otro estado.
                var estadoFinal = snap.GetValue<string>("estado") == "APROBADO" ? "APROBADO" : estado;

                tx.Update(doc, new Dictionary<string, object>
                {
                    ["id_transaccion"] = idTransaccion,
                    ["estado"] = estadoFinal,
                    ["metodo"] = metodo,
                    ["payload"] = payload,
                    ["fecha_actualizacion"] = Ahora()
                });
            }, cancellationToken: ct);
        }

        public Task<bool> ReclamarAcreditacionAsync(string referencia, TimeSpan vencimiento, CancellationToken ct = default)
        {
            var doc = Doc(referencia);
            // Firestore reintenta este callback si hay contencion. Lee el documento y, solo si sigue libre, escribe el
            // reclamo; si dos peticiones compiten, solo una confirma y la otra relee y ve el reclamo ya puesto.
            return _db.RunTransactionAsync(async tx =>
            {
                var snap = await tx.GetSnapshotAsync(doc, ct);
                if (!snap.Exists) return false;

                if (snap.GetValue<bool>("acreditado")) return false;

                if (snap.ContainsField("acreditando_desde") && snap.GetValue<Timestamp?>("acreditando_desde") is { } desde)
                {
                    if (DateTime.UtcNow - desde.ToDateTime() < vencimiento) return false;   // reclamo reciente de otro
                }

                tx.Update(doc, new Dictionary<string, object>
                {
                    ["acreditando_desde"] = Ahora(),
                    ["fecha_actualizacion"] = Ahora()
                });
                return true;
            }, cancellationToken: ct);
        }

        public Task MarcarAcreditadoAsync(string referencia, CancellationToken ct = default) =>
            Doc(referencia).UpdateAsync(new Dictionary<string, object>
            {
                ["acreditado"] = true,
                ["acreditando_desde"] = FieldValue.Delete,
                ["fecha_acreditacion"] = Ahora(),
                ["fecha_actualizacion"] = Ahora()
            }, cancellationToken: ct);

        public Task LiberarReclamoAsync(string referencia, CancellationToken ct = default) =>
            Doc(referencia).UpdateAsync(new Dictionary<string, object>
            {
                ["acreditando_desde"] = FieldValue.Delete,
                ["fecha_actualizacion"] = Ahora()
            }, cancellationToken: ct);

        private static PagoRecarga Mapear(DocumentSnapshot s) => new()
        {
            Referencia = s.GetValue<string>("referencia"),
            IdCliente = s.GetValue<long>("id_cliente"),
            IdTransaccion = s.GetValue<string>("id_transaccion"),
            Importe = s.GetValue<long>("importe"),
            Metodo = s.GetValue<string>("metodo"),
            Estado = s.GetValue<string>("estado"),
            Acreditado = s.GetValue<bool>("acreditado"),
            AcreditandoDesde = s.ContainsField("acreditando_desde") && s.GetValue<Timestamp?>("acreditando_desde") is { } t
                ? t.ToDateTime() : null,
            Fecha = s.GetValue<Timestamp>("fecha").ToDateTime()
        };
    }
}
