# SoccerBet: casa de apuestas deportivas (Liga BetPlay)

Proyecto universitario de **Bases de Datos II**. App web ASP.NET Core MVC (.NET 8). Solo futbol, solo liga colombiana.
Pagos con **Wompi en modo sandbox**. Requisito del profesor: la informacion se distribuye en **5 gestores**.
Dinero de prueba, sin dinero real.

## Alcance actual: LADO CLIENTE + SIMULACION AUTOMATICA
Se implementa lo que usa el cliente y un **simulador automatico** que juega los partidos y liquida las apuestas (ver `specs.md`).
**No construir panel de administracion** ni pantallas para registrar resultados a mano. Los partidos, cuotas y resultados salen
de datos de prueba que el simulador hace avanzar (ver "Simulador y liquidacion").

## Estructura actual del proyecto (Visual Studio)
Solucion `CasaApuestas` con un proyecto MVC (.NET 8). `CasaApuestas.sln` y `CasaApuestas.csproj` estan en la misma carpeta (raiz).
Ya existen: `Controllers/AccountController.cs`, `Models/ViewModels/LoginViewModel.cs`,
`Data/ApplicationDbContext.cs` (es el contexto de **SQL Server**: conservarlo, no renombrar por ahora), entidades como
`Login.cs`, `UsuarioEstado.cs`, `Cliente.cs` y `Saldo.cs` en `Models/`, y las vistas de login y registro. Respetar estos nombres y extenderlos.
`CLAUDE.md`, `specs.md`, `docs/` y `db/` viven en la raiz (junto al `.sln`).
La tabla `Usuario` tiene la columna `id_rol` con valor por defecto CLIENTE: el registro no necesita enviarla.

## Seguridad de configuracion
- **Nunca** escribir contrasenas ni connection strings reales en `appsettings.json` ni en archivos del repo.
  En `appsettings.json` el valor de `DefaultConnection` queda vacio o con un placeholder; el real va en **user-secrets**.
- Llaves de Wompi (`pub_test_`, `prv_test_`, `test_events_`, `test_integrity_`), credenciales de Firebase y de APIs: user-secrets.
- **El usuario `sa` esta deshabilitado y no se usa en ninguna cadena de conexion.** En SQL Server la app usa el login dedicado
  `soccerbet_app` (script `db/sqlserver/02_usuario_app.sql`), con permisos minimos: `db_datareader`, `UPDATE` solo sobre `dbo.Login` y
  `EXECUTE` sobre el esquema `dbo`. La app **no escribe directo** en `Saldo`, `Apuesta`, `Apuesta_Detalle` ni
  `Movimiento_Saldo`: el dinero solo cambia a traves de procedimientos almacenados. Si falta un permiso, pedir el minimo necesario.
- **MySQL**: la app tambien usa `soccerbet_app`, solo con `SELECT` sobre `soccerbet_catalogo.*` (`db/mysql/00_usuario.sql`). Nunca `root`.
- **user-secrets** (clave -> contenido): `ConnectionStrings:DefaultConnection` (SQL Server, `User Id=soccerbet_app`),
  `ConnectionStrings:MySql` (`User=soccerbet_app`), `Wompi:*` (llaves de pruebas y URLs del Dev Tunnel), `Firebase:ProjectId` y
  `Firebase:CredentialsPath` (el JSON de la cuenta de servicio vive fuera del proyecto, en `C:\secretos\`).
  Los scripts de `db/` solo llevan contrasenas **placeholder** (por ejemplo `CAMBIA_ESTA_CLAVE`); la clave real solo existe en user-secrets
  y en la BD. `docker-compose.yml` lee las contrasenas del contenedor desde `.env` (ignorado por Git).
- Git: un commit por cada paso que funcione. `.gitignore` debe excluir `bin/`, `obj/`, `.vs/`, `*.user`, `.env` y credenciales.

## Estado actual
- Hecho (RF1, RF2, RF3, RF11): login y registro con diseno (`PasswordHasher`, bloqueo tras 5 intentos fallidos, cliente ACTIVO),
  sesion por cookie de 8 h deslizante (claims: id_usuario, id_cliente, nombre, correo), cierre de sesion por POST con antiforgery
  (token en el encabezado `RequestVerificationToken` de los fetch de login y registro), saldo real en la cabecera
  (`SaldoService` + `WalletViewComponent`), loader global (`wwwroot/js/loader.js`), `_Layout` con cabecera y menu lateral.
- Cabecera: sin sesion muestra "Iniciar sesion" / "Registrarse" (sin saldo ni Recargar); con sesion de cliente muestra saldo,
  boton Recargar (sin accion hasta RF12), iniciales y boton de salir.
- Home publico (`[AllowAnonymous]`) con datos reales (RF4): partidos y cuotas 1X2 de PostgreSQL + nombres/escudos de MySQL, unidos en
  `PartidoService` (sin JOIN entre gestores, cache de 3 min para equipos). Las cuotas se ven pero aun no se pueden pulsar (RF9).
  Tambien hay catalogo de equipos/plantillas (`/Catalogo/Equipos`) y tabla de posiciones calculada (`/Posiciones`).
  `[Authorize]` se usa solo en las paginas del cliente (apostar, mis apuestas, billetera) cuando existan.
- Pendiente: apostar, mis apuestas, simulador y liquidacion, MongoDB, nube.
- Entidades `Cliente` y `Saldo`: SOLO LECTURA desde C# (siempre `AsNoTracking()`, nunca `SaveChanges` sobre ellas).
  Solo `Login` se actualiza desde C# (intentos fallidos, bloqueo, ultimo_login).
- Mockups de referencia (HTML): `docs/mockups/login.html` y `docs/mockups/home.html`. Las vistas Razor ya los implementan;
  si difieren (marca con icono de copa, panel izquierdo del login), manda la vista.
- Iconos: Bootstrap Icons por CDN (`<i class="bi bi-...">`). Nunca emojis en la interfaz.

## Diseno (tokens)
Fondo claro `#F6F4F7`, texto `#1E1423`, color principal **`#9ABED6`** (azul claro; la variable CSS se sigue llamando `--pink`),
`#2F6A8F` (azul oscuro) para texto, enlaces y botones de alerta; `#E3EEF5` para fondos claros; hover del boton principal `#82AAC6`.
Tipografias: Barlow Condensed (titulos) y Barlow (texto). Marca: icono de copa (`bi-trophy-fill`) + "SoccerBet".
Home: cabecera azul con hamburguesa, menu lateral ocultable, banners, pestanas, lista de partidos con cuotas 1-X-2.
Sin seccion de cupon.

## Arquitectura
**Un solo proyecto MVC (`CasaApuestas`), organizado por carpetas** (no crear proyectos separados):
`Controllers/`, `Views/`, `Models/` (entidades y ViewModels), `Services/` (logica de negocio, una clase por caso de uso),
`Data/SqlServer/`, `Data/MySql/`, `Data/Postgres/`, `Data/Mongo/`, `Data/Firebase/` (contexto/cliente y repositorios de cada gestor),
`Background/` (servicios en segundo plano: simulador y reconciliacion).
Flujo: Controller -> Service -> Repositorio (interfaz) -> gestor.
Un DbContext o cliente por gestor, registrados por inyeccion de dependencias. Los controladores solo usan interfaces
(`IApuestaRepository`, `IPartidoRepository`, `ICatalogoRepository`...), nunca saben que gestor o fuente hay detras.

| Gestor | Contenido | Libreria |
|---|---|---|
| **SQL Server** | Empresa, Rol, Usuario, Usuario_Estado, Login, Cliente, Saldo, Movimiento_Saldo, Apuesta, Apuesta_Detalle | `Microsoft.EntityFrameworkCore.SqlServer` |
| **MySQL** | deporte, liga, equipo, estadio, equipo_estadio, jugador | `Pomelo.EntityFrameworkCore.MySql` |
| **PostgreSQL** | calendario, partido, mercado, cuota | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| **MongoDB** | eventos_partido, alineaciones (con jugadores anidados), reglas | `MongoDB.Driver` |
| **Firebase (Firestore)** | pagos (antes Log_Pago) | `Google.Cloud.Firestore` |

Imagenes (escudos, fotos): `wwwroot/img` en desarrollo; Cloudinary (gratis, sin tarjeta) si se quiere nube.
**No usar Firebase Storage**: exige el plan Blaze (tarjeta). La BD solo guarda la URL (`escudo_url`, `foto_url`, `logo_url`).
Nube: un solo gestor, Azure SQL (SQL Server). Primero todo local con Docker; pasar a nube = cambiar el connection string.

## Fuente de datos de partidos y cuotas
1. **Por defecto: datos de prueba precargados** con scripts seed en `db/seed/` (Liga BetPlay: equipos, jugadores, jornadas,
   partidos con fechas relativas a "hoy", cuotas de los 3 mercados, algunas alineaciones y las reglas en Mongo).
   Son datos inventados pero realistas; sirven para desarrollar y sustentar sin depender de internet.
2. **Opcional, mas adelante: API gratuita.** API-Football tiene plan gratis (100 solicitudes/dia, sin tarjeta) y su endpoint de
   cuotas es solo prematch y se actualiza una vez al dia. **No esta verificado** que cubra cuotas de la Liga colombiana en el plan
   gratis. Si se usa: un importador separado que guarde los datos en PostgreSQL/MySQL, nunca llamarla en cada peticion web.
   La API key va en user-secrets. Hasta confirmarlo, no depender de ella.

## Reglas obligatorias
1. **Sin llaves foraneas ni JOIN entre gestores.** Las referencias entre gestores son IDs logicos (ver abajo); los datos se combinan en la capa de servicios.
2. **Dinero y apuestas solo en SQL Server**, dentro de transaccion y solo via procedimientos
   (`sp_RegistrarApuesta`, `sp_AcreditarSaldo`, `sp_ResolverApuestasPartido`).
3. Connection strings y credenciales de Wompi/Firebase/APIs en **user-secrets**, nunca en el repo.
4. Antes de cambios grandes: proponer plan y esperar aprobacion. Tareas pequenas y commits frecuentes.
5. Contrasenas con `PasswordHasher` de ASP.NET (no texto plano ni hashes caseros).
6. El codigo debe ser explicable: es un proyecto para sustentar. Comentar decisiones de arquitectura.
7. No implementar nada fuera del alcance actual (ver `specs.md`). Si algo hace falta, preguntar.
8. Home y partidos son publicos; apostar, saldo, recargar y mis apuestas requieren sesion.
9. Antes de integrar Wompi, Firestore o cualquier API externa, leer su documentacion oficial; no inventar campos ni firmas.

## IDs logicos entre gestores
- `Apuesta_Detalle.id_cuota` -> postgres.cuota
- `liga.id_empresa` -> sqlserver.Empresa
- `partido.id_equipo_local/visitante/id_estadio` -> mysql.equipo / estadio
- `calendario.id_liga` -> mysql.liga
- `eventos_partido`, `alineaciones` -> id_partido (postgres), id_equipo/id_jugador (mysql)
- `pagos.id_cliente` -> sqlserver.Cliente

## Flujos clave
**Home**: PostgreSQL entrega partidos + cuotas (`vw_cuotas_disponibles`); MySQL entrega nombres y escudos de los equipos
(cachear en memoria); la capa de servicios los une.

**Apostar** (apuesta sencilla, requiere sesion): la app lee la cuota en PostgreSQL y valida `disponible` y que el partido este
`PROGRAMADO`; luego llama a `sp_RegistrarApuesta(id_cliente, id_cuota, cuota, importe, id_apuesta OUT)` en SQL Server, que descuenta
saldo y guarda la apuesta con la cuota vigente.

**Recarga Wompi** (saga): crear doc `pagos` en Firestore (PENDIENTE, `acreditado=false`) -> checkout sandbox de Wompi ->
confirmacion del pago -> si APROBADO y `acreditado=false`: `sp_AcreditarSaldo` en SQL Server y marcar `acreditado=true`.
El webhook de Wompi no llega a `localhost`: en desarrollo usar un tunel publico o consultar el estado de la transaccion a Wompi
cuando el usuario vuelve al sitio. Validar siempre la firma/estado con Wompi. Ver `docs/firestore.md`.

## Simulador y liquidacion
Servicio en segundo plano (`BackgroundService`, carpeta `Background/`), sin pantallas de admin:
1. **Iniciar**: a la hora del partido, `PROGRAMADO` -> `EN_VIVO` (desde ahi ya no se apuesta).
2. **Simular**: tiempo acelerado y configurable en `appsettings.json` (por ejemplo 90 min de juego en ~4 min reales).
   Genera goles al azar, con probabilidad levemente ligada a las cuotas, y guarda los eventos en MongoDB (`eventos_partido`).
3. **Finalizar**: `SELECT * FROM registrar_resultado_partido(id, goles_local, goles_visitante)` en PostgreSQL deja el partido
   `FINALIZADO` y devuelve `(id_cuota, ganadora)`.
4. **Liquidar**: la app envia ese resultado como `dbo.tt_ResultadoCuota` a `sp_ResolverApuestasPartido` en SQL Server, que marca
   las apuestas GANADA/PERDIDA y acredita los premios con su movimiento de saldo.
5. **Reconciliar**: periodicamente buscar partidos FINALIZADO con apuestas aun PENDIENTE y reintentar la liquidacion. Es seguro
   repetirla: el procedimiento solo toca apuestas PENDIENTE (patron saga / consistencia eventual entre gestores).
6. **Suspendido**: si un partido pasa a SUSPENDIDO, sus apuestas pasan a ANULADA y se devuelve el importe.
7. **Renovar**: cuando no queden partidos programados, crear la siguiente jornada para que la demo nunca quede vacia.
8. Solo en `Development`: un endpoint o boton para adelantar/terminar un partido al instante (para demostrar en clase).
   No debe existir en produccion.

## Base de datos
- Scripts: `db/sqlserver/01_ajustes_sqlserver.sql` (ya ejecutado en la BD local), `db/mysql/`, `db/postgres/`, `db/mongo/`.
- Local: `docker compose up -d` (MySQL, PostgreSQL, MongoDB). SQL Server: usar el local o `--profile sqlserver`.
- Tablas en MySQL/PostgreSQL en minuscula (`ToTable("partido")`). Postgres: `timestamp` sin zona, hora local de Colombia.
- Los datos de la BD original se migran a cada gestor antes de ejecutar la "FASE FINAL" del script de SQL Server.

## Fases sugeridas
0. Solucion, user-secrets, git, `.gitignore`. (hecho)
1. Ajustes SQL Server, `_Layout` y CSS desde los mockups, login con diseno, registro, sesion y saldo (RF1-RF3, RF11). (hecho)
2. Wompi + Firestore: recarga de saldo (RF12). Necesita Firebase y llaves de Wompi sandbox; no necesita MySQL ni PostgreSQL.
3. Seeds de la Liga BetPlay + catalogo MySQL: equipos, jugadores, posiciones (RF6). Requiere Docker.
4. Partidos/cuotas en PostgreSQL + home real (RF4, RF5).
5. Apostar y Mis apuestas (RF9, RF10).
6. Simulador de partidos y liquidacion automatica (RF13, RF14), con eventos en MongoDB.
7. Detalle de partido y reglas con MongoDB (RF7, RF8).
8. Nube (Azure SQL) y, si se desea, imagenes en Cloudinary.