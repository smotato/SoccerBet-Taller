# SoccerBet: especificaciones funcionales

Contexto tecnico y reglas de arquitectura: ver `CLAUDE.md`. Este archivo dice QUE debe hacer la app.
Se trabaja por fases: implementar solo lo que se pida, y marcar aqui lo completado.

## Alcance actual: LADO CLIENTE + SIMULACION AUTOMATICA
Se construye lo que ve y usa el **cliente**, mas un **simulador automatico de partidos** que juega los partidos y
liquida las apuestas (sin intervencion humana). **No hay panel de administracion.**
Los partidos, cuotas y resultados de la Liga BetPlay se cargan con **datos de prueba precargados** (scripts seed en las BD);
el simulador los hace avanzar (PROGRAMADO -> EN_VIVO -> FINALIZADO) y renueva las jornadas. Ver "Fuente de datos" en `CLAUDE.md`.

## Roles
- **CLIENTE**: se registra, inicia sesion, recupera su contrasena, recarga saldo, apuesta, consulta partidos y sus apuestas.
- **Visitante** (sin sesion): puede ver el home y los partidos, pero no apostar, ni ver saldo, ni recargar.
- ADMIN: el rol existe en la BD pero **no hay pantallas ni funciones de admin** en este alcance.

## Requisitos funcionales
### Autenticacion (SQL Server)
- [x] RF1 Login con correo y contrasena (con diseno, PasswordHasher, bloqueo tras 5 intentos fallidos, cliente ACTIVO, cookie de 8 h).
- [x] RF2 Registro de cliente (solo mayores de 18, usar `sp_RegistrarCliente`).
- [x] RF3 Cerrar sesion (POST con antiforgery) y proteger las paginas que requieren sesion iniciada. El home es publico; `[Authorize]` se aplica solo a las paginas del cliente (apostar, mis apuestas, billetera) cuando existan.
- [ ] RF15 Recuperar contrasena (**se implementa despues de apostar y de la simulacion**; hoy el enlace "Olvide mi contrasena" del login no hace nada).
  - El cliente indica su correo y recibe un enlace o codigo de un solo uso, con vencimiento corto (por ejemplo 30 minutos).
  - La respuesta es la misma exista o no el correo, para no revelar que correos estan registrados.
  - Al cambiar la contrasena: se guarda con `PasswordHasher`, el codigo se invalida, se reinician `intentos_fallidos` y se desbloquea la cuenta si estaba bloqueada por intentos.
  - Requiere cambios en SQL Server (donde guardar el codigo y su vencimiento, guardando solo su hash, nunca el codigo en claro) y una forma de entregar el enlace: envio de correo por SMTP o, en desarrollo, mostrarlo en pantalla. Esa decision se toma al empezar este requisito.

### Billetera (SQL Server + Firebase + Wompi sandbox)
- [x] RF11 Mostrar saldo en la cabecera (saldo real de `Saldo`, formato `$ 120.000`; visitantes ven Iniciar sesion / Registrarse; el boton Recargar solo aparece con sesion de cliente).
- [x] RF12 Recargar saldo con Wompi (modo prueba). El pago se registra en Firestore y el saldo se acredita en SQL Server **una sola vez por pago** (idempotente). Sin retiros.
  - Medios de prueba: tarjeta, Nequi y PSE segun los habilite el checkout del sandbox. Un pago `PENDING` no acredita nada hasta quedar aprobado; la lista de recargas pendientes tiene un boton "Verificar".
  - Como `localhost` no es aceptado como `redirect-url` por Wompi, el retorno pasa por un Dev Tunnel y la accion publica `Puente`, que redirige a `localhost` (ver `CLAUDE.md`).

### Home y catalogo (MySQL + PostgreSQL)
- [ ] RF4 Home segun `docs/mockups/home.html`: partidos en vivo y proximos de la Liga BetPlay con cuotas 1-X-2, leidos de las BD. Publico.
- [ ] RF5 Menu lateral ocultable (3 rayitas) y filtros por pestanas.
- [ ] RF6 Tabla de posiciones, equipos y jugadores por equipo (con escudo y foto).
- [ ] RF7 Detalle de partido (solo lectura): eventos (goles, tarjetas) y alineaciones desde MongoDB y mercados disponibles.
- [ ] RF8 Pagina "Reglas / como apostar" con las reglas guardadas en MongoDB.

### Apuestas (SQL Server)
- [ ] RF9 Apostar a una cuota (apuesta sencilla). Requiere sesion. Solo partidos `PROGRAMADO` (no hay apuestas en vivo), cuota disponible y saldo suficiente.
- [ ] RF10 "Mis apuestas": historial con estado (PENDIENTE / GANADA / PERDIDA / ANULADA) y movimientos de saldo.

### Simulacion y liquidacion (PostgreSQL + MongoDB + SQL Server)
- [ ] RF13 Simulador automatico de partidos (servicio en segundo plano): inicia cada partido a su hora (EN_VIVO), simula el juego con tiempo acelerado, genera goles/eventos en MongoDB y lo finaliza con su marcador. Cuando se acaban los partidos programados, crea la siguiente jornada.
- [ ] RF14 Liquidacion automatica: al finalizar un partido, marcar las apuestas GANADA o PERDIDA y acreditar el premio al saldo del cliente, con su movimiento de saldo. Si un partido se suspende, las apuestas pasan a ANULADA y se devuelve el importe. Debe poder reintentarse sin duplicar pagos (reconciliacion periodica).

## Reglas de negocio
- El home y la lista de partidos son publicos; apostar, saldo, recargar y mis apuestas requieren sesion.
- Una cuota guardada en la apuesta no cambia aunque la cuota del partido cambie despues.
- No se puede apostar a partidos EN_VIVO / FINALIZADO / SUSPENDIDO ni a cuotas no disponibles.
- Saldo nunca negativo. Todo movimiento de dinero (apuesta, premio, recarga, devolucion) genera un registro en Movimiento_Saldo.
- Un premio o una recarga nunca se acredita dos veces.
- Los pagos son de prueba: sin retiros reales.

## Fuera de alcance (por ahora)
Panel de administracion (CRUD manual de partidos/cuotas, registrar resultados a mano), apuestas combinadas, apuestas en vivo,
retiros, otros deportes/ligas, cuotas en vivo cambiantes.

## Criterios generales
- Cumplir el diseno de `docs/mockups/` (fondo claro, color `#9ABED6`).
- Validaciones en servidor; mensajes de error en espanol.
- Sin llaves foraneas ni JOIN entre gestores.
- Los repositorios de partidos y cuotas se acceden por interfaz, para poder cambiar la fuente de datos (seed -> API) sin tocar controladores.
- El simulador y la liquidacion no son una pantalla de admin: son procesos del sistema. Un endpoint de "adelantar partido" solo puede existir en modo Development.