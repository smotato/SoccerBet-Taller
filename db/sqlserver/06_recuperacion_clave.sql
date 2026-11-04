/* =====================================================================
   SoccerBet - Recuperar contrasena (RF15) en SQL Server
   Ejecutar UNA VEZ como administrador sobre CasaApuestas (despues del 05). Se puede repetir sin romper nada.

   Que crea:
     - Tabla dbo.Recuperacion_Clave: SOLO el hash SHA-256 (hex) del codigo, su vencimiento y si ya se uso.
       El codigo en claro viaja unicamente en el enlace del correo; nunca se guarda.
     - sp_SolicitarRecuperacion: crea el codigo (invalida el anterior) con limite de solicitudes por usuario.
     - sp_ObtenerRecuperacion:   devuelve el hash de un codigo VIGENTE (para compararlo en la app en tiempo constante).
     - sp_RestablecerClave:      cambia la contrasena, reinicia intentos, desbloquea y consume el codigo, todo atomico.

   Permisos: soccerbet_app ya tiene EXECUTE sobre el esquema dbo y db_datareader (02_usuario_app.sql); no hace falta
   ningun GRANT nuevo. La app NO escribe directo en la tabla: solo a traves de estos procedimientos.
   Todas las fechas son UTC (SYSUTCDATETIME) para no depender de la zona horaria del servidor.
   ===================================================================== */
USE [CasaApuestas];
GO

/* 1. Tabla */
IF OBJECT_ID('dbo.Recuperacion_Clave') IS NULL
BEGIN
    CREATE TABLE dbo.Recuperacion_Clave (
        id_recuperacion BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Recuperacion_Clave PRIMARY KEY,
        id_login        INT        NOT NULL CONSTRAINT FK_Recuperacion_Login REFERENCES dbo.[Login](id_login),
        codigo_hash     CHAR(64)   NOT NULL,                                   -- SHA-256 en hexadecimal minuscula
        creado          DATETIME2  NOT NULL CONSTRAINT DF_Recuperacion_Creado DEFAULT SYSUTCDATETIME(),
        vence           DATETIME2  NOT NULL,
        usado           BIT        NOT NULL CONSTRAINT DF_Recuperacion_Usado DEFAULT 0,  -- 1 = ya usado O reemplazado por uno nuevo
        usado_en        DATETIME2  NULL,
        ip_solicitud    VARCHAR(45) NULL
    );
END
GO

/* Un solo codigo vigente (usado = 0) por usuario: la propia BD lo garantiza. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Recuperacion_Vigente' AND object_id = OBJECT_ID('dbo.Recuperacion_Clave'))
    CREATE UNIQUE INDEX UX_Recuperacion_Vigente ON dbo.Recuperacion_Clave(id_login) WHERE usado = 0;
GO
/* Para contar las solicitudes recientes de un usuario. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Recuperacion_Login_Creado' AND object_id = OBJECT_ID('dbo.Recuperacion_Clave'))
    CREATE INDEX IX_Recuperacion_Login_Creado ON dbo.Recuperacion_Clave(id_login, creado);
GO

/* 2. Solicitar: crea un codigo nuevo e invalida el anterior.
      @creado = 0 cuando el correo no existe o se alcanzo el limite (3 por hora): la app responde IGUAL en todos los casos. */
CREATE OR ALTER PROCEDURE dbo.sp_SolicitarRecuperacion
    @email           NVARCHAR(150),
    @codigo_hash     CHAR(64),
    @minutos_vigencia INT,
    @ip              VARCHAR(45),
    @max_por_hora    INT,
    @creado          BIT           OUTPUT,
    @nombre          NVARCHAR(100) OUTPUT      -- nombres del usuario, solo para el saludo del correo
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @creado = 0; SET @nombre = NULL;

    DECLARE @id_login INT, @ahora DATETIME2 = SYSUTCDATETIME();

    SELECT @id_login = l.id_login, @nombre = u.nombres
    FROM dbo.[Login] l
    JOIN dbo.Usuario u ON u.id_usuario = l.id_usuario
    WHERE l.username = LOWER(LTRIM(RTRIM(@email)));

    IF @id_login IS NULL RETURN;

    BEGIN TRAN;

    /* Serializa las solicitudes del mismo usuario (evita que dos peticiones simultaneas choquen con el indice unico). */
    EXEC sp_getapplock @Resource = @email, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;

    IF (SELECT COUNT(*) FROM dbo.Recuperacion_Clave WHERE id_login = @id_login AND creado > DATEADD(HOUR, -1, @ahora)) >= @max_por_hora
    BEGIN
        COMMIT; SET @nombre = NULL; RETURN;
    END

    /* El codigo anterior (si seguia vigente) queda invalidado. */
    UPDATE dbo.Recuperacion_Clave SET usado = 1, usado_en = @ahora WHERE id_login = @id_login AND usado = 0;

    INSERT dbo.Recuperacion_Clave (id_login, codigo_hash, creado, vence, ip_solicitud)
    VALUES (@id_login, @codigo_hash, @ahora, DATEADD(MINUTE, @minutos_vigencia, @ahora), @ip);

    COMMIT;
    SET @creado = 1;
END
GO

/* 3. Obtener: devuelve (una fila) el hash del codigo SOLO si esta vigente: no usado y no vencido.
      La app lo compara con el hash del codigo recibido usando comparacion en tiempo constante. */
CREATE OR ALTER PROCEDURE dbo.sp_ObtenerRecuperacion
    @id_recuperacion BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT codigo_hash
    FROM dbo.Recuperacion_Clave
    WHERE id_recuperacion = @id_recuperacion AND usado = 0 AND vence > SYSUTCDATETIME();
END
GO

/* 4. Restablecer: valida otra vez (hash, vigencia, no usado), cambia la contrasena y consume el codigo en UNA transaccion.
      @resultado: 'OK' o 'INVALIDO' (codigo incorrecto, vencido, ya usado o inexistente: no se distingue). */
CREATE OR ALTER PROCEDURE dbo.sp_RestablecerClave
    @id_recuperacion BIGINT,
    @codigo_hash     CHAR(64),
    @password_hash   NVARCHAR(500),
    @resultado       VARCHAR(10) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @resultado = 'INVALIDO';
    DECLARE @id_login INT, @ahora DATETIME2 = SYSUTCDATETIME();

    BEGIN TRAN;

    /* Una sola sentencia atomica: solo UNA ejecucion concurrente logra marcar el codigo como usado. */
    UPDATE dbo.Recuperacion_Clave
       SET usado = 1, usado_en = @ahora, @id_login = id_login
     WHERE id_recuperacion = @id_recuperacion
       AND codigo_hash = @codigo_hash COLLATE Latin1_General_BIN2
       AND usado = 0 AND vence > @ahora;

    IF @@ROWCOUNT = 1
    BEGIN
        UPDATE dbo.[Login]
           SET password_hash = @password_hash, intentos_fallidos = 0, bloqueado = 0
         WHERE id_login = @id_login;
        SET @resultado = 'OK';
    END

    COMMIT;
END
GO

/* Verificacion rapida */
SELECT name FROM sys.procedures WHERE name IN ('sp_SolicitarRecuperacion','sp_ObtenerRecuperacion','sp_RestablecerClave');
SELECT COUNT(*) AS columnas FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Recuperacion_Clave');
GO
