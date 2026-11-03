# Firebase (Firestore): pagos Wompi (reemplaza la tabla Log_Pago)

Coleccion `pagos`. **ID del documento = `{proveedor}_{referencia}`** (ej. `wompi_SB-12-3f9a...`).
La referencia la genera SoccerBet ANTES de pagar, asi el documento existe desde el inicio (el `id_transaccion` de Wompi aun
no se conoce y se llena cuando el usuario vuelve del checkout). Reemplaza la restriccion UNIQUE(proveedor, id_transaccion):
al ser el ID determinista, crear dos veces el mismo pago falla y un evento repetido solo actualiza el mismo documento.
Wompi solo permite consultar una transaccion por su id (`GET /v1/transactions/{id}`), no por referencia.

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
| acreditando_desde | timestamp (opcional) | "reclamo": hora en que una peticion empezo a acreditar. Se borra al terminar. Un reclamo con mas de `Wompi:ReclamoVencimientoMinutos` (2) se considera abandonado y se puede retomar |
| fecha_acreditacion | timestamp (opcional) | cuando se marco `acreditado=true` |

Indice compuesto: `id_cliente ASC, fecha DESC`.

Flujo de recarga (saga):
1. La app crea el documento en estado `PENDIENTE` con `acreditado=false`.
2. El usuario paga en el Web Checkout y vuelve a `/Recarga/Retorno?id=...` (o pulsa "Verificar" en la lista de pendientes).
   La app consulta la transaccion REAL a Wompi (llave privada), valida referencia, cliente, monto y moneda, y actualiza `estado`.
   (Segunda etapa: el webhook de Wompi, validado con `Wompi:EventsSecret`, hara esta misma actualizacion.)
3. Si `APROBADO` y `acreditado=false`:
   a. **Reclamar** (transaccion de Firestore): si nadie tiene un reclamo vigente, escribe `acreditando_desde`. Solo una peticion lo logra.
   b. Ejecuta `sp_AcreditarSaldo` con concepto unico `Recarga Wompi {referencia}`. El SP es idempotente por concepto: si ya
      existe responde `ya_acreditado = 1` sin sumar.
   c. Marca `acreditado=true` y borra el reclamo.
   Si la app se cae entre (b) y (c), el reclamo vence a los 2 min, se reintenta y el SP responde "ya acreditado": nunca se suma dos veces.

Reglas de seguridad: **denegar todo acceso desde clientes** (`allow read, write: if false;`).
Solo el servidor accede con el Admin SDK (credenciales en user-secrets, nunca en el repo).
