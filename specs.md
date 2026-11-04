# SoccerBet: especificaciones funcionales

Contexto tecnico y reglas de arquitectura: ver `CLAUDE.md`. Este archivo dice QUE debe hacer la app.
Se trabaja por fases: implementar solo lo que se pida, y marcar aqui lo completado.

## Alcance actual: LADO CLIENTE + SIMULACION AUTOMATICA
Se construye lo que ve y usa el **cliente**, mas un **simulador automatico de partidos** que juega los partidos y
liquida las apuestas (sin intervencion humana). **No hay panel de administracion.**
Los partidos, cuotas y resultados de la Liga BetPlay se cargan con **datos de prueba precargados** (scripts seed en las BD);
el simulador los hace avanzar (PROGRAMADO -> EN_VIVO -> FINALIZADO) y renueva las jornadas. Ver "Fuente de datos" en `CLAUDE.md`.
Se puede apostar **antes y durante** el partido (apuestas en vivo con cuotas que cambian segun marcador y minuto) y las apuestas pueden
ser **sencillas o combinadas**. Ver "Reglas de apuestas" mas abajo.

## Roles
- **CLIENTE**: se registra, inicia sesion, recupera su contrasena, recarga saldo, apuesta, consulta partidos y sus apuestas.
- **Visitante** (sin sesion): puede ver el home y los partidos, pero no apostar, ni ver saldo, ni recargar.
- ADMIN: el rol existe en la BD pero **no hay pantallas ni funciones de admin** en este alcance.

## Requisitos funcionales
### Autenticacion (SQL Server)
- [x] RF1 Login con correo y contrasena (con diseno, PasswordHasher, bloqueo tras 5 intentos fallidos, cliente ACTIVO, cookie de 8 h).
- [x] RF2 Registro de cliente (solo mayores de 18, usar `sp_RegistrarCliente`).
- [x] RF3 Cerrar sesion (POST con antiforgery) y proteger las paginas que requieren sesion iniciada. El home es publico; `[Authorize]` se aplica solo a las paginas del cliente (apostar, mis apuestas, billetera) cuando existan.
- [x] RF15 Recuperar contrasena (**se implementa despues de apostar y de la simulacion**; hoy el enlace "Olvide mi contrasena" del login no hace nada).
  - El cliente indica su correo y recibe un enlace o codigo de un solo uso, con vencimiento corto (por ejemplo 30 minutos).
  - La respuesta es la misma exista o no el correo, para no revelar que correos estan registrados.
  - Al cambiar la contrasena: se guarda con `PasswordHasher`, el codigo se invalida, se reinician `intentos_fallidos` y se desbloquea la cuenta si estaba bloqueada por intentos.
  - Requiere cambios en SQL Server (donde guardar el codigo y su vencimiento, guardando solo su hash, nunca el codigo en claro) y una forma de entregar el enlace: envio de correo por SMTP o, en desarrollo, mostrarlo en pantalla. Esa decision se toma al empezar este requisito.

### Billetera (SQL Server + Firebase + Wompi sandbox)
- [x] RF11 Mostrar saldo en la cabecera (saldo real de `Saldo`, formato `$ 120.000`; visitantes ven Iniciar sesion / Registrarse; el boton Recargar solo aparece con sesion de cliente).
- [x] RF12 Recargar saldo con Wompi (modo prueba). El pago se registra en Firestore y el saldo se acredita en SQL Server **una sola vez por pago** (idempotente). Los retiros son un requisito aparte (RF16).
  - Medios de prueba: tarjeta, Nequi y PSE segun los habilite el checkout del sandbox. Un pago `PENDING` no acredita nada hasta quedar aprobado; la lista de recargas pendientes tiene un boton "Verificar".
  - Como `localhost` no es aceptado como `redirect-url` por Wompi, el retorno pasa por un Dev Tunnel y la accion publica `Puente`, que redirige a `localhost` (ver `CLAUDE.md`).

### Home y catalogo (MySQL + PostgreSQL)
- [x] RF4 Home segun `docs/mockups/home.html`: partidos en vivo y proximos de la Liga BetPlay con cuotas 1-X-2, leidos de las BD. Publico.
- [ ] RF5 Menu lateral ocultable (3 rayitas) y filtros por pestanas.
- [ ] RF6 Tabla de posiciones, equipos y jugadores por equipo (con escudo y foto).
- [x] RF7 Detalle de partido: marcador y minuto, eventos (goles, tarjetas) y alineaciones desde MongoDB, y **todos los mercados del partido con sus cuotas, que se pueden agregar al cupon** (1X2, Total de goles 2.5 y Ambos equipos anotan; no hay mas mercados). Se llega haciendo clic en un partido del home o de la lista. Publico para ver; apostar requiere sesion.
- [x] RF8 Pagina "Reglas / como apostar" con las reglas guardadas en MongoDB.

### Apuestas (SQL Server)
- [x] RF16 Retiro de saldo **simulado**. Pagina "Retirar" (boton junto a Recargar): importe (minimo $ 10.000, maximo el saldo) y medio simulado (Nequi, PSE o cuenta bancaria). Se aprueba **al instante**: `sp_RetirarSaldo` descuenta el saldo una sola vez (idempotente, nunca negativo) y registra un DEBITO "Retiro simulado". Lista "Mis retiros". Sin Wompi ni Firestore.
- [x] RF17 Cash out: el cliente cierra una apuesta PENDIENTE y cobra un valor calculado al momento (boton "Cash out $ X" en Mis apuestas).
  - Valor = importe x producto de (cuota guardada / cuota actual) por seleccion x 0,95 (margen configurable). Seleccion GANADA: cuota actual 1,00; ANULADA: no cuenta. Tope: la ganancia potencial. Pesos enteros.
  - Disponible solo si ninguna seleccion perdio y las pendientes tienen mercado **abierto**. **El boton se bloquea cuando el valor sea menor a $ 1** (o el mercado este cerrado).
  - Al confirmar, el servidor recalcula; si el valor cambio, pide aceptar el nuevo. `sp_CashOut` marca CASHOUT solo si sigue PENDIENTE, guarda `valor_cashout` y acredita una sola vez. La liquidacion ignora apuestas que ya no estan PENDIENTE.
- [x] RF9 Apostar con un cupon. Requiere sesion. Una seleccion = apuesta **sencilla**; 2 o mas selecciones = apuesta **combinada** (varias del mismo partido, con la regla de "una por mercado"). Partidos `PROGRAMADO` o `EN_VIVO` con cuota disponible y saldo suficiente.
  - La cuota la lee siempre el servidor en PostgreSQL en el momento de apostar; nunca se confia en la que envia el navegador. Si la cuota cambio desde que el cliente la vio, se le muestra la nueva y debe aceptarla.
  - Un doble clic o un reenvio no debe crear dos apuestas (clave de idempotencia por cupon).
  - Solo se descuenta el importe una vez, por el procedimiento almacenado, dentro de una transaccion.
- [x] RF10 "Mis apuestas": historial con estado (PENDIENTE / GANADA / PERDIDA / ANULADA / CASHOUT), las selecciones de cada combinada con su estado individual, y movimientos de saldo.

### Simulacion y liquidacion (PostgreSQL + MongoDB + SQL Server)
- [x] RF13 Simulador automatico de partidos (servicio en segundo plano): inicia cada partido a su hora (EN_VIVO), simula el juego con tiempo acelerado, genera goles/eventos en MongoDB y lo finaliza con su marcador. Cuando se acaban los partidos programados, crea la siguiente jornada.
  - **Cuotas en vivo**: mientras el partido esta EN_VIVO, el simulador recalcula las cuotas cada pocos segundos segun marcador y minuto (modelo de probabilidad sencillo, margen de la casa fijo, cuota minima 1.01). Si un resultado queda casi decidido, su cuota se marca no disponible.
  - **Cierre de mercados**: las cuotas se suspenden unos segundos despues de cada gol y se cierran del todo a partir del minuto 85 simulado, hasta el final del partido. Al finalizar quedan no disponibles.
  - El minuto simulado y el marcador son visibles en el home y en el detalle del partido (la pagina se actualiza cada pocos segundos).
  - Los tiempos dependen de la velocidad de simulacion y son configurables: duracion de un partido, minuto de cierre (85), pausa tras gol e intervalo de actualizacion (~5 s, menor que la pausa tras gol).
  - **El simulador nunca suspende ni anula partidos por su cuenta.**
- [x] RF18 Modo demostracion (solo `Development`): en el detalle de partido, recuadro con "Terminar partido ahora" y "Anular partido". Anular pasa el partido a SUSPENDIDO ("Anulado"), cierra mercados y devuelve: sencillas ANULADA con devolucion del importe; en combinadas la seleccion sale y la cuota total se recalcula; si todas quedan anuladas, se devuelve el importe.
- [x] RF14 Liquidacion automatica: al finalizar un partido, resolver sus selecciones (GANADA / PERDIDA) y, segun el tipo de apuesta, la apuesta completa; acreditar el premio al saldo del cliente, con su movimiento de saldo.
  - Sencilla: GANADA o PERDIDA segun su unica seleccion.
  - Combinada: PERDIDA en cuanto una seleccion pierde; GANADA solo cuando **todas** las selecciones ganan; el premio es importe x cuota total. Una seleccion ANULADA (partido suspendido) sale de la combinada (su cuota cuenta como 1.00); si todas quedan anuladas, la apuesta pasa a ANULADA y se devuelve el importe.
  - Debe poder reintentarse sin duplicar pagos (reconciliacion periodica).

## Reglas de negocio
- El home, la lista de partidos y el detalle de partido son publicos; apostar, saldo, recargar y mis apuestas requieren sesion.
- Una cuota guardada en la apuesta no cambia aunque la cuota del partido cambie despues. En una combinada, cada seleccion guarda su propia cuota y la cuota total es el producto.
- No se puede apostar a partidos FINALIZADO / SUSPENDIDO, ni a cuotas no disponibles o con mercado cerrado. Los partidos PROGRAMADO y EN_VIVO si admiten apuestas.
- Saldo nunca negativo. Todo movimiento de dinero (apuesta, premio, recarga, devolucion) genera un registro en Movimiento_Saldo.
- Un premio o una recarga nunca se acredita dos veces.
- Los pagos y retiros son simulados: no se mueve dinero real. Un retiro y un cash out se aplican una sola vez.

## Reglas de apuestas (valores configurables en `appsettings.json`)
- Importe minimo $ 1.000 y maximo $ 500.000 por apuesta (valores por defecto).
- Combinada: de 2 a 10 selecciones, la cuota total es el producto. Se pueden elegir varias del mismo partido pero **como maximo una por mercado en cada partido** (hasta 3: 1X2, Total de goles, Ambos anotan), asi no hay selecciones imposibles (Local + Empate). Se valida en el servidor; en el cupon, otra cuota del mismo mercado reemplaza a la anterior. No se modela la correlacion entre mercados (limitacion conocida).
- Premio maximo por apuesta (por defecto $ 20.000.000): si el importe x cuota total lo supera, se paga el maximo y el cupon lo avisa antes de confirmar.
- Apuesta en vivo: la cuota que se paga es la vigente al confirmar. Si cambio, el cliente la acepta o cancela. El cash out se calcula con la cuota vigente (RF17).
- El cupon muestra la ganancia potencial incluyendo el importe apostado.

## Fuera de alcance (por ahora)
Panel de administracion (CRUD manual de partidos/cuotas, registrar resultados a mano), retiros reales, otros deportes/ligas,
mercados distintos de 1X2, Total de goles 2.5 y Ambos equipos anotan, modelar correlacion entre selecciones del mismo partido,
transmision de video.

## Criterios generales
- Cumplir el diseno de `docs/mockups/` (fondo claro, color `#9ABED6`).
- Validaciones en servidor; mensajes de error en espanol.
- Sin llaves foraneas ni JOIN entre gestores.
- Los repositorios de partidos y cuotas se acceden por interfaz, para poder cambiar la fuente de datos (seed -> API) sin tocar controladores.
- El simulador y la liquidacion no son una pantalla de admin: son procesos del sistema. Un endpoint de "adelantar partido" solo puede existir en modo Development.