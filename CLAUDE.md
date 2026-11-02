# SoccerBet: casa de apuestas deportivas (Liga BetPlay)

Proyecto universitario de **Bases de Datos II**. App web ASP.NET Core MVC (.NET 8). Solo futbol, solo liga colombiana.
Pagos con **Wompi en modo sandbox**. Requisito del profesor: la informacion se distribuye en **5 gestores**.
Dinero de prueba, sin dinero real.

## Alcance actual: SOLO LADO CLIENTE
Se implementa unicamente lo que usa el cliente (ver `specs.md`). **No construir panel de administracion**, ni registro de
resultados, ni liquidacion de apuestas. Los partidos, cuotas y resultados salen de datos de prueba (ver "Fuente de datos").

## Estructura actual del proyecto (Visual Studio)
Solucion `CasaApuestas` con un proyecto MVC (.NET 8). Ya existen: `Controllers/AccountController.cs`, `LoginViewModel.cs`,
`Data/ApplicationDbContext.cs` (es el contexto de **SQL Server**: conservarlo, no renombrar por ahora), entidades como
`Login.cs` y `UsuarioEstado.cs` en `Models/`, y la vista `Login.cshtml`. Respetar estos nombres y extenderlos.
`CLAUDE.md`, `specs.md`, `docs/` y `db/` viven en la raiz de la solucion (junto al `.sln`).
La tabla `Usuario` tiene la columna `id_rol` con valor por defecto CLIENTE: el registro no necesita enviarla.

## Seguridad de configuracion
- **Nunca** escribir contrasenas ni connection strings reales en `appsettings.json` ni en archivos del repo.
  En `appsettings.json` el valor de `DefaultConnection` queda vacio o con un placeholder; el real va en user-secrets.
- No usar el usuario `sa`; usar un login de SQL Server dedicado con permisos solo sobre la BD `CasaApuestas`.

## Estado actual
- Hecho (RF1, RF2, RF3, RF11): login y registro con diseno (`PasswordHasher`, bloqueo tras 5 intentos fallidos, cliente ACTIVO),
  sesion por cookie de 8 h deslizante (claims: id_usuario, id_cliente, nombre, correo), cierre de sesion por POST con antiforgery
  (token en el encabezado `RequestVerificationToken` de los fetch de login y registro), saldo real en la cabecera
  (`SaldoService` + `WalletViewComponent`), loader global (`wwwroot/js/loader.js`), `_Layout` con cabecera y menu lateral.
- Home publico (`[AllowAnonymous]`) con 3 partidos de ejemplo FIJOS en la vista: aun sin datos reales.
  `[Authorize]` se usa solo en las paginas del cliente (apostar, mis apuestas, billetera) cuando existan.
- Pendiente: catalogo (MySQL), home con partidos y cuotas reales (PostgreSQL), apostar, mis apuestas, recarga Wompi, nube.
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
`Data/SqlServer/`, `Data/MySql/`, `Data/Postgres/`, `Data/Mongo/`, `Data/Firebase/` (contexto/cliente y repositorios de cada gestor).
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
1. **Por defecto (alcance actual): datos de prueba precargados** con scripts seed en `db/seed/` (Liga BetPlay: equipos,
   jugadores, jornadas, partidos en distintos estados, cuotas de los 3 mercados, algunos eventos/alineaciones en Mongo).
   Son datos inventados pero realistas; sirven para desarrollar y sustentar sin depender de internet.
2. **Opcional, mas adelante: API gratuita.** API-Football tiene plan gratis (100 solicitudes/dia, sin tarjeta) y su endpoint de
   cuotas es solo prematch y se actualiza una vez al dia. **No esta verificado** que cubra cuotas de la Liga colombiana en el plan
   gratis. Si se usa: un importador separado que guarde los datos en PostgreSQL/MySQL, nunca llamarla en cada peticion web.
   La API key va en user-secrets. Hasta confirmarlo, no depender de ella.

## Reglas obligatorias
1. **Sin llaves foraneas ni JOIN entre gestores.** Las referencias entre gestores son IDs logicos (ver abajo); los datos se combinan en la capa de servicios.
2. **Dinero y apuestas solo en SQL Server**, dentro de transaccion (`sp_RegistrarApuesta`, `sp_AcreditarSaldo`).
3. Connection strings y credenciales de Wompi/Firebase/APIs en **user-secrets**, nunca en el repo.
4. Antes de cambios grandes: proponer plan y esperar aprobacion. Tareas pequenas y commits frecuentes.
5. Contrasenas con `PasswordHasher` de ASP.NET (no texto plano ni hashes caseros).
6. El codigo debe ser explicable: es un proyecto para sustentar. Comentar decisiones de arquitectura.
7. No implementar nada fuera del alcance actual (ver `specs.md`). Si algo hace falta, preguntar.

## IDs logicos entre gestores
- `Apuesta_Detalle.id_cuota` -> postgres.cuota
- `liga.id_empresa` -> sqlserver.Empresa
- `partido.id_equipo_local/visitante/id_estadio` -> mysql.equipo / estadio
- `calendario.id_liga` -> mysql.liga
- `eventos_partido`, `alineaciones` -> id_partido (postgres), id_equipo/id_jugador (mysql)
- `pagos.id_cliente` -> sqlserver.Cliente

## Flujos clave (alcance actual)
**Home**: PostgreSQL entrega partidos + cuotas (`vw_cuotas_disponibles`); MySQL entrega nombres y escudos de los equipos
(cachear en memoria); la capa de servicios los une.

**Apostar** (apuesta sencilla): la app lee la cuota en PostgreSQL y valida `disponible` y estado del partido; luego llama a
`sp_RegistrarApuesta(id_cliente, id_cuota, cuota, importe)` en SQL Server, que descuenta saldo y guarda la apuesta con la cuota vigente.

**Recarga Wompi** (saga): crear doc `pagos` en Firestore (PENDIENTE) -> checkout sandbox de Wompi -> webhook (validar firma) ->
si APROBADO y `acreditado=false`: `sp_AcreditarSaldo` en SQL Server y marcar `acreditado=true`. Ver `docs/firestore.md`.

**Fuera de alcance ahora** (existe en la BD, no usar): `registrar_resultado_partido` (PostgreSQL) y `sp_ResolverApuestasPartido`
(SQL Server) para liquidar apuestas desde un futuro panel admin.

## Base de datos
- Scripts: `db/sqlserver/01_ajustes_sqlserver.sql` (ejecutar sobre copia de la BD actual), `db/mysql/`, `db/postgres/`, `db/mongo/`.
- Local: `docker compose up -d` (MySQL, PostgreSQL, MongoDB). SQL Server: usar el local o `--profile sqlserver`.
- Tablas en MySQL/PostgreSQL en minuscula (`ToTable("partido")`). Postgres: `timestamp` sin zona, hora local de Colombia.
- Los datos de la BD original se migran a cada gestor antes de ejecutar la "FASE FINAL" del script de SQL Server.

## Fases sugeridas (solo cliente)
0. Solucion por capas, user-secrets, docker compose.
1. Ajustes SQL Server, `_Layout` y CSS desde los mockups, login con diseno, registro (RF1-RF3).
2. Seeds de la Liga BetPlay + catalogo MySQL: equipos, jugadores, posiciones (RF6).
3. Partidos/cuotas en PostgreSQL + home (RF4, RF5).
4. Apostar y Mis apuestas (RF9, RF10).
5. Wompi + Firestore: saldo y recarga (RF11, RF12).
6. Detalle de partido y reglas con MongoDB (RF7, RF8).
7. Nube (Azure SQL) y, si se desea, imagenes en Cloudinary.