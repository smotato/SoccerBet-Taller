# SoccerBet: especificaciones funcionales

Contexto tecnico y reglas de arquitectura: ver `CLAUDE.md`. Este archivo dice QUE debe hacer la app.
Se trabaja por fases: implementar solo lo que se pida, y marcar aqui lo completado.

## Alcance actual: SOLO EL LADO CLIENTE
Por ahora se construye unicamente lo que ve y usa el **cliente**. No hay panel de administracion.
Los partidos, cuotas y resultados de la Liga BetPlay vienen de **datos de prueba precargados** (scripts seed
en las BD) y, opcionalmente mas adelante, de una API gratuita. Ver "Fuente de datos" en `CLAUDE.md`.

## Rol
- **CLIENTE**: se registra, inicia sesion, recarga saldo, apuesta, consulta partidos y sus apuestas.
- ADMIN: el rol existe en la BD pero **no hay pantallas ni funciones de admin** en este alcance.

## Requisitos funcionales (alcance actual)
### Autenticacion (SQL Server)
- [x] RF1 Login con correo y contrasena (con diseno, PasswordHasher, bloqueo tras 5 intentos fallidos, cliente ACTIVO, cookie de 8 h).
- [x] RF2 Registro de cliente (solo mayores de 18, usar `sp_RegistrarCliente`).
- [x] RF3 Cerrar sesion (POST con antiforgery) y proteger las paginas que requieren sesion iniciada. El home es publico; `[Authorize]` se aplica solo a las paginas del cliente (apostar, mis apuestas, billetera) cuando existan.

### Home y catalogo (MySQL + PostgreSQL)
- [ ] RF4 Home segun `docs/mockups/home.html`: partidos en vivo y proximos de la Liga BetPlay con cuotas 1-X-2.
- [ ] RF5 Menu lateral ocultable (3 rayitas) y filtros por pestanas.
- [ ] RF6 Tabla de posiciones, equipos y jugadores por equipo (con escudo y foto).
- [ ] RF7 Detalle de partido (solo lectura): eventos y alineaciones desde MongoDB (datos de prueba) y mercados disponibles.
- [ ] RF8 Pagina "Reglas / como apostar" con las reglas guardadas en MongoDB.

### Apuestas (SQL Server)
- [ ] RF9 Apostar a una cuota (apuesta sencilla): validar partido PROGRAMADO o EN_VIVO permitido, cuota disponible, saldo suficiente.
- [ ] RF10 "Mis apuestas": historial con estado y movimientos de saldo. Las apuestas quedan PENDIENTE (no hay liquidacion en este alcance).

### Billetera (SQL Server + Firebase + Wompi sandbox)
- [x] RF11 Mostrar saldo en la cabecera (saldo real de `Saldo`, formato `$ 120.000`; visitantes ven Iniciar sesion / Registrarse).
- [ ] RF12 Recargar saldo con Wompi (modo prueba). Acreditar una sola vez por pago (idempotente).

## Reglas de negocio
- Una cuota guardada en la apuesta no cambia aunque la cuota del partido cambie despues.
- No se puede apostar a partidos FINALIZADO / SUSPENDIDO ni a cuotas no disponibles.
- Saldo nunca negativo. Todo movimiento de dinero genera un registro en Movimiento_Saldo.
- Los pagos son de prueba: sin retiros reales.

## Fuera de alcance (por ahora)
Panel de administracion (CRUD de partidos/cuotas, registrar resultados), liquidacion de apuestas y pago de premios,
apuestas combinadas, retiros, otros deportes/ligas, cuotas en vivo cambiantes.
(La BD ya trae `sp_ResolverApuestasPartido` y `registrar_resultado_partido` para una fase futura: NO usarlos ahora.)

## Criterios generales
- Cumplir el diseno de `docs/mockups/` (fondo claro, color `#9ABED6`).
- Validaciones en servidor; mensajes de error en espanol.
- Sin llaves foraneas ni JOIN entre gestores.
- Los repositorios de partidos y cuotas se acceden por interfaz, para poder cambiar la fuente de datos (seed -> API) sin tocar controladores.
