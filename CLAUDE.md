# SoccerBet: casa de apuestas deportivas (Liga BetPlay)

Proyecto universitario de **Bases de Datos II**. App web ASP.NET Core MVC (.NET 8). Solo futbol, solo liga colombiana.
Pagos con **Wompi en modo sandbox**. Requisito del profesor: la informacion se distribuye en **5 gestores**.
Dinero de prueba, sin dinero real.

## Alcance actual: LADO CLIENTE + SIMULACION AUTOMATICA
Se implementa lo que usa el cliente y un **simulador automatico** que juega los partidos, mueve las cuotas en vivo y liquida las apuestas
(ver `specs.md`). Hay **apuestas sencillas y combinadas, antes y durante el partido**.
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
- **MySQL**: la app tambien usa `soccerbet_app`, solo con `SELECT` sobre `soccerbet_catalogo.*` (`db/mysql/00_usuario.sql`, ignorado por Git). Nunca `root`.
- **PostgreSQL**: `soccerbet_app` con `SELECT` en todo e `INSERT/UPDATE` solo en `calendario`, `partido` y `cuota` (`db/postgres/03_usuario.sql`, ignorado por Git). Nunca `postgres`.
- **MongoDB**: la app usa el usuario `soccerbet_app` con rol `readWrite` solo sobre la base `soccerbet` (`db/mongo/03_usuario.js`, ignorado por
  Git; en el repo solo `03_usuario.js.ejemplo`). Nunca `root`. En la fase actual solo se lee (reglas); la escritura se usara para eventos.
- **user-secrets** (clave -> contenido): `ConnectionStrings:DefaultConnection` (SQL Server, `User Id=soccerbet_app`),
  `ConnectionStrings:MySql` (`User=soccerbet_app`), `ConnectionStrings:Postgres` y `ConnectionStrings:Mongo` (ambas con `soccerbet_app`), `Wompi:*` (llaves de pruebas y URLs del Dev Tunnel), `Firebase:ProjectId` y
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
- Apostar (RF9) y Mis apuestas (RF10) hechos para partidos PROGRAMADO: detalle de partido `/Partidos/Detalle/{id}`, cupon sencillo/combinado
  (home y detalle), `POST /Apuestas/Confirmar` (validacion en servidor, idempotencia por clave, `sp_RegistrarApuesta`) y `/Apuestas`
  (Mis apuestas, solo lectura de `vw_DetalleApuestas` y `Movimiento_Saldo`). Limites en `appsettings.json` (`Apuestas`).
- Retiros simulados (RF16) hechos: boton "Retirar" en la cabecera y pagina `/Billetera/Retirar` (formulario con clave de idempotencia,
  `sp_RetirarSaldo`, lista "Mis retiros"). El minimo esta en `appsettings.json` (`Retiros`); el maximo es el saldo.
- Pagina publica de reglas (RF8) hecha: `/Reglas` lee de MongoDB (`soccerbet.reglas`, solo reglas activas) con `ReglaRepository` (solo
  lectura), cache en memoria de unos minutos (`Mongo:CacheMinutos`) y `IMongoClient` singleton perezoso (la app arranca aunque Mongo
  este caido; `Mongo:ServerSelectionTimeoutSeconds` evita que la pagina se cuelgue). Enlazada desde el menu lateral, "Ayuda" y el cupon.
- Simulador (RF13, etapa E1 de la fase 6) hecho: `Background/SimuladorHostedService` + `Services/Simulador/` (opciones en `appsettings.json`, seccion
  `Simulador`). Inicia los PROGRAMADO vencidos de forma escalonada, avanza el minuto, genera goles y tarjetas (jugadores reales de MySQL) con
  eventos en MongoDB (`eventos_partido`, upsert idempotente) y finaliza con `registrar_resultado_partido`. Un candado de PostgreSQL
  (`BloqueoSimulador`) asegura UNA sola instancia simulando. Los EN_VIVO se retoman desde su minuto guardado (NULL -> `MinutoSinDato`=35).
  `Simulador:AdelantarPartidosProgramados` (true solo en Development) adelanta el siguiente partido para que la demo no quede quieta.
  OJO: en cuanto la app corre con `Simulador:Habilitado=true` simula sobre la BD configurada; para pruebas usar una copia de Postgres.
- Liquidacion (RF14, etapa E2) hecha: al finalizar un partido el simulador envia `(id_cuota, ganadora)` a `sp_ResolverApuestasPartido`
  (`Services/Simulador/LiquidadorPartidos`, `Data/SqlServer/LiquidacionRepository`). Una RECONCILIACION periodica
  (`Simulador:IntervaloReconciliacionSegundos`) busca selecciones PENDIENTE de partidos FINALIZADO (reenvia el resultado) o SUSPENDIDO
  (`sp_AnularApuestasPartido`); es segura de repetir (patron saga). Cuando no quedan PROGRAMADO, `GeneradorJornada` crea la siguiente
  jornada (round-robin de los equipos de MySQL, estadio del local, 7 cuotas por partido con Poisson y `Simulador:MargenCasa`).
- Pendiente (fase 6): cuotas en vivo (E3), modo demostracion (E4); apuestas en vivo y cash out (6b);
  alineaciones y detalle de eventos (RF7); nube.
- Entidades `Cliente` y `Saldo`: SOLO LECTURA desde C# (siempre `AsNoTracking()`, nunca `SaveChanges` sobre ellas).
  Solo `Login` se actualiza desde C# (intentos fallidos, bloqueo, ultimo_login).
- Mockups de referencia (HTML): `docs/mockups/login.html`, `docs/mockups/home.html`, `docs/mockups/equipos.html` y
  `docs/mockups/apuestas.html` (cupon, combinadas, detalle de partido y Mis apuestas). Las vistas Razor de login, home y equipos
  ya los implementan; si difieren (marca con icono de copa, panel izquierdo del login), manda la vista.
- Iconos: Bootstrap Icons por CDN (`<i class="bi bi-...">`). Nunca emojis en la interfaz.

## Diseno (tokens)
Fondo claro `#F6F4F7`, texto `#1E1423`, color principal **`#9ABED6`** (azul claro; la variable CSS se sigue llamando `--pink`),
`#2F6A8F` (azul oscuro) para texto, enlaces y botones de alerta; `#E3EEF5` para fondos claros; hover del boton principal `#82AAC6`.
Tipografias: Barlow Condensed (titulos) y Barlow (texto). Marca: icono de copa (`bi-trophy-fill`) + "SoccerBet".
Home: cabecera azul con hamburguesa, menu lateral ocultable, banners, pestanas, lista de partidos con cuotas 1-X-2.
**Cupon de apuesta** (panel lateral en escritorio, bajo la lista en movil): selecciones, importe, cuota total, ganancia potencial y
avisos de error; ver `docs/mockups/apuestas.html`. Detalle de partido con todos los mercados: `docs/mockups/apuestas.html` (vista Partido).

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
   (`sp_RegistrarApuesta`, `sp_AcreditarSaldo`, `sp_ResolverApuestasPartido`). `sp_RegistrarApuesta` recibe **una lista de selecciones**
   (tipo de tabla) para soportar combinadas; ver `db/sqlserver/03_apuestas_combinadas.sql` (se crea en la fase de apuestas).
3. Connection strings y credenciales de Wompi/Firebase/APIs en **user-secrets**, nunca en el repo.
4. Antes de cambios grandes: proponer plan y esperar aprobacion. Tareas pequenas y commits frecuentes.
5. Contrasenas con `PasswordHasher` de ASP.NET (no texto plano ni hashes caseros).
6. El codigo debe ser explicable: es un proyecto para sustentar. Comentar decisiones de arquitectura.
7. No implementar nada fuera del alcance actual (ver `specs.md`). Si algo hace falta, preguntar.
8. Home y partidos son publicos; apostar, saldo, recargar y mis apuestas requieren sesion.
9. Antes de integrar Wompi, Firestore o cualquier API externa, leer su documentacion oficial; no inventar campos ni firmas.
10. **Apostar se valida siempre en el servidor**: cuota, estado del partido y mercado se leen de PostgreSQL en el momento de confirmar;
    el navegador solo envia ids de cuota, importe y la cuota que vio (para detectar cambios). Una clave de idempotencia por cupon
    evita duplicar apuestas, retiros y cash outs por doble clic o reenvio; el valor del cash out tambien se calcula en el servidor. No se habilitan apuestas en vivo hasta que existan las cuotas en vivo (fase 6).

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

**Apostar** (sencilla o combinada, requiere sesion): el cupon envia la lista de `id_cuota`, la cuota que vio el cliente de cada una,
el importe y una clave de idempotencia. El servidor lee en PostgreSQL cada cuota actual y valida: cuota `disponible`, partido
`PROGRAMADO` o `EN_VIVO` con mercado abierto, **como maximo una seleccion por mercado en cada partido** (hasta 3 por partido; el servidor conoce partido y mercado de cada cuota), de 2 a 10 selecciones en combinada y limites de importe.
Si alguna cuota difiere de la que vio el cliente, responde con las nuevas para que las acepte. Luego calcula la cuota total (producto) y
llama a `sp_RegistrarApuesta(id_cliente, selecciones TVP (id_cuota, cuota), importe, clave, id_apuesta OUT)` en SQL Server, que en una
transaccion descuenta el saldo, crea `Apuesta` (cuota_total, posible_ganancia) y una fila de `Apuesta_Detalle` por seleccion.

**Cash out** (RF17): valor = importe x producto de (cuota guardada / cuota actual) x 0,95 (ganadas: 1; anuladas: no cuentan; tope = ganancia potencial). Solo si ninguna seleccion perdio y las pendientes tienen mercado abierto; **se bloquea si el valor es menor a $ 1**. El servidor recalcula al confirmar; si cambio, pide aceptar. `sp_CashOut` marca `CASHOUT` solo si sigue `PENDIENTE` (nueva columna `valor_cashout`) y acredita una vez.

**Retiro simulado** (RF16): pagina "Retirar" (minimo $ 10.000, maximo el saldo). `sp_RetirarSaldo(id_cliente, importe, clave, ...)` descuenta y registra DEBITO "Retiro simulado" de forma idempotente, al instante. Sin Wompi ni Firestore.

**Cuotas en vivo**: el simulador (unico que escribe cuotas en PostgreSQL) las recalcula cada pocos segundos con un modelo de
probabilidad sencillo segun marcador y minuto, con margen fijo y minimo 1.01. El home y el detalle de partido consultan por `fetch`
cada ~5 s (marcador, minuto y cuotas); no se usa SignalR salvo que se pida despues.

**Recarga Wompi** (saga): crear doc `pagos` en Firestore (PENDIENTE, `acreditado=false`) -> checkout sandbox de Wompi ->
confirmacion del pago -> si APROBADO y `acreditado=false`: `sp_AcreditarSaldo` en SQL Server y marcar `acreditado=true`.
El webhook de Wompi no llega a `localhost`: en desarrollo usar un tunel publico o consultar el estado de la transaccion a Wompi
cuando el usuario vuelve al sitio. Validar siempre la firma/estado con Wompi. Ver `docs/firestore.md`.

## Simulador y liquidacion
Servicio en segundo plano (`BackgroundService`, carpeta `Background/`), sin pantallas de admin:
1. **Iniciar**: a la hora del partido, `PROGRAMADO` -> `EN_VIVO` (se sigue pudiendo apostar mientras el mercado este abierto).
2. **Simular**: tiempo acelerado y configurable en `appsettings.json` (por ejemplo 90 min de juego en ~4 min reales).
   Genera goles al azar, con probabilidad levemente ligada a las cuotas, y guarda los eventos en MongoDB (`eventos_partido`).
   El minuto simulado se guarda en el partido (columna nueva en PostgreSQL, script `db/postgres/04_ajustes_vivo.sql`).
2b. **Cuotas en vivo y cierre**: cada pocos segundos recalcula las cuotas (marcador + minuto restante), suspende las cuotas unos segundos
   tras cada gol y las cierra a partir del minuto 85 (`disponible = FALSE`). Una cuota casi segura se marca no disponible.
3. **Finalizar**: `SELECT * FROM registrar_resultado_partido(id, goles_local, goles_visitante)` en PostgreSQL deja el partido
   `FINALIZADO` y devuelve `(id_cuota, ganadora)`.
4. **Liquidar**: la app envia ese resultado como `dbo.tt_ResultadoCuota` a `sp_ResolverApuestasPartido` en SQL Server, que marca
   las apuestas GANADA/PERDIDA y acredita los premios con su movimiento de saldo.
5. **Reconciliar**: periodicamente buscar partidos FINALIZADO con apuestas aun PENDIENTE y reintentar la liquidacion. Es seguro
   repetirla: el procedimiento solo toca apuestas PENDIENTE (patron saga / consistencia eventual entre gestores).
6. **Suspendido**: si un partido pasa a SUSPENDIDO, sus selecciones pasan a ANULADA (sencilla: se devuelve el importe; combinada: la seleccion sale). **El simulador nunca suspende solo**: unicamente el boton de demostracion (paso 8).
7. **Renovar**: cuando no queden partidos programados, crear la siguiente jornada para que la demo nunca quede vacia.
8. Solo en `Development`: en el detalle de partido, recuadro "Modo demostracion" con **Terminar partido ahora** y **Anular partido** (RF18).
   No debe existir en produccion (comprobar `IHostEnvironment.IsDevelopment()` en vista y endpoint).
9. **Configuracion** en `appsettings.json`: duracion real de un partido, minuto de cierre (85), pausa tras gol (segundos reales) e intervalo de actualizacion (~5 s, menor que la pausa tras gol).

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
5. **5a** Apostar y Mis apuestas (RF9, RF10) en partidos `PROGRAMADO`: cupon sencillo/combinado con la regla "una por mercado", nuevo
   `sp_RegistrarApuesta` con lista de selecciones (`db/sqlserver/03_apuestas_combinadas.sql`), detalle de partido con los 3 mercados
   (sin eventos ni alineaciones aun), Mis apuestas con selecciones. **5b** Retiros simulados (RF16).
6. Simulador con cuotas en vivo y cierre de mercados, liquidacion automatica de sencillas y combinadas (RF13, RF14), eventos en MongoDB y
   modo demostracion (RF18). Incluye ajustar `sp_ResolverApuestasPartido` (combinadas, selecciones anuladas) y el `fetch` periodico.
6b. Apuestas en vivo (habilitar `EN_VIVO` en el cupon) y cash out (RF17). Solo despues de la fase 6.
7. Detalle de partido: eventos y alineaciones (MongoDB, RF7) y reglas (RF8), incluidas las reglas de apuestas.
8. Nube (Azure SQL) y, si se desea, imagenes en Cloudinary.
9. Recuperar contrasena (RF15), al final.

## Flujo de trabajo (cada fase)
1. Primero los **scripts de BD** (`db/...`) y el **mockup** si hay pantalla nueva; se prueban y se hace commit.
2. Prompt a Claude Code: leer `CLAUDE.md`, `specs.md`, el script y el mockup, y entregar un **plan corto**; no escribe codigo hasta el OK.
3. Se revisa el plan (alcance, seguridad, nombres exactos de tablas y columnas, validaciones en servidor) y se da el OK, a veces por etapas.
4. Implementa explicando cada archivo, **prueba con resultados concretos** y reporta.
5. Se verifica en el navegador (escritorio y movil), se marca el RF en `specs.md` y se hace **un commit** por paso que funcione.
6. Los scripts con contrasenas nunca se suben a Git.