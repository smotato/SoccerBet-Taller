# Firebase (Firestore): pagos Wompi (reemplaza la tabla Log_Pago)

Coleccion `pagos`. **ID del documento = `{proveedor}_{id_transaccion}`** (ej. `wompi_11004-1730400000-12345`).
Reemplaza la restriccion UNIQUE(proveedor, id_transaccion): si el webhook llega repetido, se actualiza el mismo documento.

| Campo | Tipo | Notas |
|---|---|---|
| id_cliente | number | ID logico -> SQL Server.Cliente |
| proveedor | string | `wompi` |
| id_transaccion | string | ID de la transaccion en Wompi |
| referencia | string | referencia que genera SoccerBet al crear el pago |
| importe | number | pesos COP (> 0) |
| tipo | string | `DEPOSITO` |
| metodo | string | `PSE`, `CARD`, `NEQUI`... |
| estado | string | `PENDIENTE`, `APROBADO`, `RECHAZADO`, `ERROR` (mapeado desde el estado de Wompi) |
| acreditado | bool | `true` cuando ya se sumo al saldo (evita acreditar dos veces) |
| payload | map | respuesta/webhook completo de Wompi |
| fecha, fecha_actualizacion | timestamp | |

Indice compuesto: `id_cliente ASC, fecha DESC`.

Flujo de recarga (saga):
1. La app crea el documento en estado `PENDIENTE` con `acreditado=false`.
2. Wompi (sandbox) llama al webhook; la app valida la firma del evento y actualiza `estado`.
3. Si `APROBADO` y `acreditado=false`: ejecuta `sp_AcreditarSaldo` en SQL Server y marca `acreditado=true` (en una transaccion de Firestore).

Reglas de seguridad: **denegar todo acceso desde clientes** (`allow read, write: if false;`).
Solo el servidor accede con el Admin SDK (credenciales en user-secrets, nunca en el repo).
