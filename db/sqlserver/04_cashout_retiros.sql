/* =====================================================================
   SoccerBet - Cash out (RF17) y retiros simulados (RF16) en SQL Server
   Ejecutar UNA VEZ como administrador sobre CasaApuestas (despues del 03). Se puede repetir sin romper nada.
   La app los usara en: retiros = fase 5b, cash out = fase 6b.
   ===================================================================== */
USE [CasaApuestas];
GO

/* 1. Apuesta: nuevo estado CASHOUT y valor cobrado */
IF COL_LENGTH('dbo.Apuesta','valor_cashout') IS NULL
    ALTER TABLE dbo.Apuesta ADD valor_cashout DECIMAL(18,2) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Apuesta_Estado')
    ALTER TABLE dbo.Apuesta DROP CONSTRAINT CK_Apuesta_Estado;
ALTER TABLE dbo.Apuesta WITH NOCHECK ADD CONSTRAINT CK_Apuesta_Estado
    CHECK (estado IN ('PENDIENTE','GANADA','PERDIDA','ANULADA','CASHOUT'));
GO

/* 2. Tabla de retiros (historial "Mis retiros") */
IF OBJECT_ID('dbo.Retiro') IS NULL
BEGIN
    CREATE TABLE dbo.Retiro (
        id_retiro BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Retiro PRIMARY KEY,
        id_cliente INT NOT NULL CONSTRAINT FK_Retiro_Cliente REFERENCES dbo.Cliente(id_cliente),
        importe DECIMAL(18,2) NOT NULL CONSTRAINT CK_Retiro_Importe CHECK (importe > 0),
        medio NVARCHAR(30) NOT NULL,                       -- Nequi / PSE / Cuenta bancaria (simulado)
        estado VARCHAR(20) NOT NULL CONSTRAINT DF_Retiro_Estado DEFAULT 'APROBADO',
        clave NVARCHAR(64) NOT NULL,                       -- idempotencia
        fecha DATETIME2 NOT NULL CONSTRAINT DF_Retiro_Fecha DEFAULT SYSDATETIME()
    );
    CREATE UNIQUE INDEX UX_Retiro_Cliente_Clave ON dbo.Retiro (id_cliente, clave);
END
GO

/* 3. Retiro simulado: se aprueba al instante */
CREATE OR ALTER PROCEDURE dbo.sp_RetirarSaldo
    @id_cliente INT,
    @importe    DECIMAL(18,2),
    @medio      NVARCHAR(30),
    @clave      NVARCHAR(64),
    @minimo     DECIMAL(18,2),          -- minimo de retiro (appsettings)
    @id_retiro  BIGINT OUTPUT,
    @repetido   BIT    OUTPUT           -- 1 = esa clave ya existia: no se descontó de nuevo
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @id_saldo BIGINT, @saldo DECIMAL(18,2);
    SET @repetido = 0;

    IF @importe <= 0 OR @importe < @minimo THROW 50030, N'El importe es menor al minimo de retiro.', 1;
    IF @clave IS NULL OR LEN(@clave) = 0   THROW 50027, N'Falta la clave de idempotencia.', 1;
    IF NOT EXISTS (SELECT 1 FROM Cliente WHERE id_cliente = @id_cliente AND estado = 'ACTIVO')
        THROW 50021, N'El cliente no existe o no está activo.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT @id_saldo = id_saldo, @saldo = saldo_actual
        FROM Saldo WITH (UPDLOCK, HOLDLOCK) WHERE id_cliente = @id_cliente;

        SELECT @id_retiro = id_retiro FROM Retiro WHERE id_cliente = @id_cliente AND clave = @clave;
        IF @id_retiro IS NOT NULL
        BEGIN
            SET @repetido = 1;
            COMMIT TRANSACTION;
            RETURN;
        END

        IF @saldo IS NULL OR @saldo < @importe
            THROW 50031, N'Saldo insuficiente para el retiro.', 1;

        INSERT INTO Retiro (id_cliente, importe, medio, clave) VALUES (@id_cliente, @importe, @medio, @clave);
        SET @id_retiro = SCOPE_IDENTITY();

        UPDATE Saldo SET saldo_actual = saldo_actual - @importe, fecha_actualizacion = SYSDATETIME()
        WHERE id_saldo = @id_saldo;
        INSERT INTO Movimiento_Saldo (id_saldo, importe, tipo, concepto)
        VALUES (@id_saldo, @importe, 'DEBITO', CONCAT(N'Retiro simulado #', @id_retiro, N' (', @medio, N')'));
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* 4. Cash out: cierra una apuesta PENDIENTE y acredita el valor que calculo la app con las cuotas vigentes */
CREATE OR ALTER PROCEDURE dbo.sp_CashOut
    @id_cliente INT,
    @id_apuesta BIGINT,
    @valor      DECIMAL(18,2),          -- valor recalculado por el servidor
    @repetido   BIT OUTPUT              -- 1 = ya estaba en CASHOUT: no se acredito de nuevo
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @id_saldo BIGINT, @estado VARCHAR(30), @posible DECIMAL(18,2);
    SET @repetido = 0;

    IF @valor < 1 THROW 50032, N'El valor de cash out es menor a $ 1.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT @id_saldo = id_saldo FROM Saldo WITH (UPDLOCK, HOLDLOCK) WHERE id_cliente = @id_cliente;
        IF @id_saldo IS NULL THROW 50012, N'El cliente no tiene saldo creado.', 1;

        SELECT @estado = estado, @posible = posible_ganancia
        FROM Apuesta WITH (UPDLOCK, HOLDLOCK) WHERE id_apuesta = @id_apuesta AND id_cliente = @id_cliente;

        IF @estado IS NULL THROW 50033, N'La apuesta no existe.', 1;
        IF @estado = 'CASHOUT'
        BEGIN
            SET @repetido = 1;
            COMMIT TRANSACTION;
            RETURN;
        END
        IF @estado <> 'PENDIENTE'
            THROW 50034, N'La apuesta ya se resolvio; no se puede hacer cash out.', 1;
        IF @valor > @posible SET @valor = @posible;            -- nunca mas que la ganancia potencial

        UPDATE Apuesta SET estado = 'CASHOUT', valor_cashout = @valor, fecha_resolucion = SYSDATETIME()
        WHERE id_apuesta = @id_apuesta;

        UPDATE Saldo SET saldo_actual = saldo_actual + @valor, fecha_actualizacion = SYSDATETIME()
        WHERE id_saldo = @id_saldo;
        INSERT INTO Movimiento_Saldo (id_saldo, id_apuesta, importe, tipo, concepto)
        VALUES (@id_saldo, @id_apuesta, @valor, 'CREDITO', CONCAT(N'Cash out apuesta #', @id_apuesta));
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* 5. Verificacion: ambos procedimientos deben aparecer, y la tabla Retiro con 7 columnas */
SELECT name FROM sys.procedures WHERE name IN ('sp_RetirarSaldo','sp_CashOut');
SELECT COUNT(*) AS columnas_retiro FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Retiro');
GO
