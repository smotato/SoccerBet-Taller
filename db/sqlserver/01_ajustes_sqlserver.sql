/* =====================================================================
   SoccerBet - SQL Server (CasaApuestas): ajustes para la arquitectura de 5 gestores
   EJECUTAR PRIMERO SOBRE UNA COPIA (backup) DE LA BD. Ejecutar por bloques (GO).
   Se queda en SQL Server: Empresa, Usuario, Usuario_Estado, Login, Cliente,
                           Saldo, Movimiento_Saldo, Apuesta, Apuesta_Detalle, Rol (nueva)
   ===================================================================== */
USE [CasaApuestas];
GO

/* ---------- 1. Roles ---------- */
IF OBJECT_ID('dbo.Rol') IS NULL
BEGIN
    CREATE TABLE dbo.Rol (
        id_rol INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Rol PRIMARY KEY,
        nombre NVARCHAR(50) NOT NULL CONSTRAINT UQ_Rol_Nombre UNIQUE
    );
    INSERT INTO dbo.Rol (nombre) VALUES (N'ADMIN'), (N'CLIENTE');   -- ADMIN=1, CLIENTE=2
END
GO
IF COL_LENGTH('dbo.Usuario','id_rol') IS NULL
BEGIN
    ALTER TABLE dbo.Usuario ADD id_rol INT NOT NULL CONSTRAINT DF_Usuario_Rol DEFAULT 2;
    ALTER TABLE dbo.Usuario ADD CONSTRAINT FK_Usuario_Rol FOREIGN KEY (id_rol) REFERENCES dbo.Rol(id_rol);
END
GO

/* ---------- 2. Apuesta_Detalle: sin Empresa, sin FK a Cuota (vive en PostgreSQL), con estado ---------- */
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ApuestaDetalle_Cuota')
    ALTER TABLE dbo.Apuesta_Detalle DROP CONSTRAINT FK_ApuestaDetalle_Cuota;   -- id_cuota pasa a ser ID logico
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ApuestaDetalle_Empresa')
    ALTER TABLE dbo.Apuesta_Detalle DROP CONSTRAINT FK_ApuestaDetalle_Empresa;
IF COL_LENGTH('dbo.Apuesta_Detalle','id_empresa') IS NOT NULL
    ALTER TABLE dbo.Apuesta_Detalle DROP COLUMN id_empresa;
IF COL_LENGTH('dbo.Apuesta_Detalle','estado') IS NULL
    ALTER TABLE dbo.Apuesta_Detalle ADD estado VARCHAR(20) NOT NULL CONSTRAINT DF_ApuestaDetalle_Estado DEFAULT 'PENDIENTE';
GO
-- Sincroniza el estado de detalles ya existentes (apuestas sencillas)
UPDATE d SET d.estado = a.estado
FROM dbo.Apuesta_Detalle d INNER JOIN dbo.Apuesta a ON a.id_apuesta = d.id_apuesta
WHERE a.estado IN ('GANADA','PERDIDA');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_ApuestaDetalle_Estado')
    ALTER TABLE dbo.Apuesta_Detalle WITH NOCHECK ADD CONSTRAINT CK_ApuestaDetalle_Estado
        CHECK (estado IN ('PENDIENTE','GANADA','PERDIDA','ANULADA'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_Apuesta_Estado')
    ALTER TABLE dbo.Apuesta WITH NOCHECK ADD CONSTRAINT CK_Apuesta_Estado
        CHECK (estado IN ('PENDIENTE','GANADA','PERDIDA','ANULADA'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_MovimientoSaldo_Tipo')
    ALTER TABLE dbo.Movimiento_Saldo WITH NOCHECK ADD CONSTRAINT CK_MovimientoSaldo_Tipo
        CHECK (tipo IN ('DEBITO','CREDITO'));
GO

/* ---------- 3. Concurrencia en Saldo y logo de la empresa ---------- */
IF COL_LENGTH('dbo.Saldo','row_version') IS NULL
    ALTER TABLE dbo.Saldo ADD row_version ROWVERSION;
IF COL_LENGTH('dbo.Empresa','logo_url') IS NULL
    ALTER TABLE dbo.Empresa ADD logo_url NVARCHAR(500) NULL;
GO

/* ---------- 4. Tipo para resultados de cuotas (lo envia la app desde PostgreSQL) ---------- */
IF TYPE_ID('dbo.tt_ResultadoCuota') IS NULL
    CREATE TYPE dbo.tt_ResultadoCuota AS TABLE (id_cuota INT NOT NULL PRIMARY KEY, ganadora BIT NOT NULL);
GO

/* ---------- 5. sp_RegistrarApuesta: la app valida cuota/partido en PostgreSQL y envia la cuota ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_RegistrarApuesta
    @id_cliente INT,
    @id_cuota   INT,
    @cuota      DECIMAL(10,4),    -- cuota vigente leida de PostgreSQL por la app
    @importe    DECIMAL(18,2),
    @id_apuesta BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @id_saldo BIGINT, @saldo DECIMAL(18,2), @posible_ganancia DECIMAL(18,2);

    IF @importe <= 0 THROW 50020, N'El importe de la apuesta debe ser mayor que cero.', 1;
    IF @cuota <= 1   THROW 50023, N'La cuota recibida no es valida.', 1;
    IF NOT EXISTS (SELECT 1 FROM Cliente WHERE id_cliente = @id_cliente AND estado = 'ACTIVO')
        THROW 50021, N'El cliente no existe o no está activo.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT @id_saldo = id_saldo, @saldo = saldo_actual
        FROM Saldo WITH (UPDLOCK, HOLDLOCK) WHERE id_cliente = @id_cliente;
        IF @saldo IS NULL OR @saldo < @importe
            THROW 50024, N'Saldo insuficiente para realizar la apuesta.', 1;

        SET @posible_ganancia = dbo.fn_GananciaPotencial(@importe, @cuota);
        INSERT INTO Apuesta (id_cliente, importe, cuota_total, posible_ganancia, estado)
        VALUES (@id_cliente, @importe, @cuota, @posible_ganancia, 'PENDIENTE');
        SET @id_apuesta = SCOPE_IDENTITY();

        INSERT INTO Apuesta_Detalle (id_apuesta, id_cuota, cuota) VALUES (@id_apuesta, @id_cuota, @cuota);

        UPDATE Saldo SET saldo_actual = saldo_actual - @importe, fecha_actualizacion = SYSDATETIME()
        WHERE id_saldo = @id_saldo;
        INSERT INTO Movimiento_Saldo (id_saldo, id_apuesta, importe, tipo, concepto)
        VALUES (@id_saldo, @id_apuesta, @importe, 'DEBITO', CONCAT(N'Apuesta #', @id_apuesta, N' registrada'));
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ---------- 6. sp_ResolverApuestasPartido: recibe que cuotas ganaron/perdieron ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_ResolverApuestasPartido
    @resultados dbo.tt_ResultadoCuota READONLY
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @ahora DATETIME2 = SYSDATETIME();
    DECLARE @ganadas TABLE (id_apuesta BIGINT, id_cliente INT, premio DECIMAL(18,2));
    BEGIN TRY
        BEGIN TRANSACTION;
        -- 1) Resultado de cada seleccion
        UPDATE d SET d.estado = IIF(r.ganadora = 1, 'GANADA', 'PERDIDA')
        FROM Apuesta_Detalle d
        INNER JOIN @resultados r ON r.id_cuota = d.id_cuota
        INNER JOIN Apuesta a ON a.id_apuesta = d.id_apuesta
        WHERE a.estado = 'PENDIENTE' AND d.estado = 'PENDIENTE';

        -- 2) Perdidas: al menos una seleccion perdida
        UPDATE a SET a.estado = 'PERDIDA', a.fecha_resolucion = @ahora
        FROM Apuesta a
        WHERE a.estado = 'PENDIENTE'
          AND EXISTS (SELECT 1 FROM Apuesta_Detalle d WHERE d.id_apuesta = a.id_apuesta AND d.estado = 'PERDIDA');

        -- 3) Ganadas: todas las selecciones acertadas (y alguna pertenece a este lote)
        UPDATE a SET a.estado = 'GANADA', a.fecha_resolucion = @ahora
        OUTPUT inserted.id_apuesta, inserted.id_cliente, inserted.posible_ganancia INTO @ganadas
        FROM Apuesta a
        WHERE a.estado = 'PENDIENTE'
          AND EXISTS (SELECT 1 FROM Apuesta_Detalle d INNER JOIN @resultados r ON r.id_cuota = d.id_cuota
                      WHERE d.id_apuesta = a.id_apuesta)
          AND NOT EXISTS (SELECT 1 FROM Apuesta_Detalle d WHERE d.id_apuesta = a.id_apuesta AND d.estado <> 'GANADA');

        -- 4) Pagar premios
        UPDATE s SET s.saldo_actual = s.saldo_actual + g.total, s.fecha_actualizacion = @ahora
        FROM Saldo s
        INNER JOIN (SELECT id_cliente, SUM(premio) AS total FROM @ganadas GROUP BY id_cliente) g
                ON g.id_cliente = s.id_cliente;
        INSERT INTO Movimiento_Saldo (id_saldo, id_apuesta, importe, tipo, concepto)
        SELECT s.id_saldo, g.id_apuesta, g.premio, 'CREDITO', CONCAT(N'Premio de la apuesta #', g.id_apuesta)
        FROM @ganadas g INNER JOIN Saldo s ON s.id_cliente = g.id_cliente;
        COMMIT TRANSACTION;

        SELECT (SELECT COUNT(*) FROM @ganadas) AS apuestas_ganadas;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ---------- 7. sp_AcreditarSaldo: reemplaza a sp_RealizarDeposito (el log del pago vive en Firebase) ---------- */
CREATE OR ALTER PROCEDURE dbo.sp_AcreditarSaldo
    @id_cliente INT,
    @importe    DECIMAL(18,2),
    @concepto   NVARCHAR(250)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @id_saldo BIGINT;
    IF @importe <= 0 THROW 50010, N'El importe debe ser mayor que cero.', 1;
    IF NOT EXISTS (SELECT 1 FROM Cliente WHERE id_cliente = @id_cliente AND estado = 'ACTIVO')
        THROW 50011, N'El cliente no existe o no está activo.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT @id_saldo = id_saldo FROM Saldo WITH (UPDLOCK, HOLDLOCK) WHERE id_cliente = @id_cliente;
        IF @id_saldo IS NULL THROW 50012, N'El cliente no tiene saldo creado.', 1;
        UPDATE Saldo SET saldo_actual = saldo_actual + @importe, fecha_actualizacion = SYSDATETIME()
        WHERE id_saldo = @id_saldo;
        INSERT INTO Movimiento_Saldo (id_saldo, importe, tipo, concepto)
        VALUES (@id_saldo, @importe, 'CREDITO', @concepto);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ---------- 8. vw_DetalleApuestas sin dependencias de otros gestores (la app completa partido/equipo) ---------- */
CREATE OR ALTER VIEW dbo.vw_DetalleApuestas AS
SELECT a.id_apuesta, a.id_cliente, c.documento, dbo.fn_NombreCompleto(c.id_usuario) AS cliente,
       a.fecha_apuesta, a.importe, a.cuota_total, a.posible_ganancia, a.estado AS estado_apuesta,
       d.id_detalle, d.id_cuota, d.cuota, d.estado AS estado_seleccion
FROM Apuesta a
INNER JOIN Cliente c ON c.id_cliente = a.id_cliente
INNER JOIN Apuesta_Detalle d ON d.id_apuesta = a.id_apuesta;
GO

/* =====================================================================
   FASE FINAL (NO ejecutar hasta migrar los datos a MySQL / PostgreSQL / Mongo / Firebase)
   Objetos que dependen de tablas que se van de SQL Server:
   ---------------------------------------------------------------------
   DROP PROCEDURE dbo.sp_RealizarDeposito, dbo.sp_RegistrarResultadoPartido;
   DROP VIEW dbo.vw_PartidosDetallados, dbo.vw_CuotasDisponibles, dbo.vw_EstadisticasJugadores;
   DROP FUNCTION dbo.fn_CuotaGanadora, dbo.fn_ResultadoPartido, dbo.fn_TablaPosiciones, dbo.fn_PartidosPorEquipo;
   -- Tablas migradas (orden por dependencias):
   DROP TABLE dbo.Alineacion_Detalle, dbo.Alineacion, dbo.Evento_Partido, dbo.Cuota, dbo.Mercado,
              dbo.Partido, dbo.Calendario, dbo.Jugador, dbo.Equipo_Estadio, dbo.Equipo, dbo.Estadio,
              dbo.Liga, dbo.Regla, dbo.Deporte, dbo.Log_Pago;
   ===================================================================== */
