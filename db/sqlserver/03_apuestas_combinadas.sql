/* =====================================================================
   SoccerBet - Fase 5a: apuestas sencillas y combinadas (SQL Server)
   Ejecutar UNA VEZ en SSMS / Azure Data Studio sobre la BD CasaApuestas, con un usuario administrador (no soccerbet_app).
   Se puede ejecutar de nuevo sin romper nada.
   ===================================================================== */
USE [CasaApuestas];
GO

/* 1. Clave de idempotencia: el mismo cupon enviado dos veces crea una sola apuesta */
IF COL_LENGTH('dbo.Apuesta','clave_idempotencia') IS NULL
    ALTER TABLE dbo.Apuesta ADD clave_idempotencia NVARCHAR(64) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Apuesta_Cliente_Clave')
    CREATE UNIQUE INDEX UX_Apuesta_Cliente_Clave ON dbo.Apuesta (id_cliente, clave_idempotencia)
    WHERE clave_idempotencia IS NOT NULL;
GO

/* 2. Tipo de tabla con las selecciones del cupon (la app lo llena con cuotas leidas de PostgreSQL) */
IF TYPE_ID('dbo.tt_SeleccionApuesta') IS NULL
    CREATE TYPE dbo.tt_SeleccionApuesta AS TABLE (
        id_cuota INT           NOT NULL PRIMARY KEY,   -- la PK impide repetir la misma cuota
        cuota    DECIMAL(10,4) NOT NULL
    );
GO

/* 3. Registrar apuesta (sencilla = 1 seleccion, combinada = 2 a 10) */
CREATE OR ALTER PROCEDURE dbo.sp_RegistrarApuesta
    @id_cliente      INT,
    @selecciones     dbo.tt_SeleccionApuesta READONLY,
    @importe         DECIMAL(18,2),
    @clave           NVARCHAR(64),             -- clave de idempotencia (un GUID por cupon)
    @premio_maximo   DECIMAL(18,2),            -- tope de premio (appsettings)
    @id_apuesta      BIGINT        OUTPUT,
    @cuota_total     DECIMAL(18,4) OUTPUT,
    @posible_ganancia DECIMAL(18,2) OUTPUT,
    @repetida        BIT           OUTPUT      -- 1 = esa clave ya existia: no se cobro de nuevo
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @id_saldo BIGINT, @saldo DECIMAL(18,2), @n INT, @producto FLOAT;
    SET @repetida = 0;

    SELECT @n = COUNT(*) FROM @selecciones;
    IF @n < 1 OR @n > 10        THROW 50025, N'El cupon debe tener entre 1 y 10 selecciones.', 1;
    IF @importe <= 0            THROW 50020, N'El importe de la apuesta debe ser mayor que cero.', 1;
    IF @clave IS NULL OR LEN(@clave) = 0 THROW 50027, N'Falta la clave de idempotencia.', 1;
    IF EXISTS (SELECT 1 FROM @selecciones WHERE cuota <= 1)
        THROW 50023, N'Alguna cuota recibida no es valida.', 1;
    IF NOT EXISTS (SELECT 1 FROM Cliente WHERE id_cliente = @id_cliente AND estado = 'ACTIVO')
        THROW 50021, N'El cliente no existe o no está activo.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        -- Bloquea el saldo del cliente: serializa cupones simultaneos del mismo cliente
        SELECT @id_saldo = id_saldo, @saldo = saldo_actual
        FROM Saldo WITH (UPDLOCK, HOLDLOCK) WHERE id_cliente = @id_cliente;

        -- Idempotencia: si esta clave ya creo una apuesta, devolverla sin cobrar otra vez
        SELECT @id_apuesta = id_apuesta, @cuota_total = cuota_total, @posible_ganancia = posible_ganancia
        FROM Apuesta WHERE id_cliente = @id_cliente AND clave_idempotencia = @clave;
        IF @id_apuesta IS NOT NULL
        BEGIN
            SET @repetida = 1;
            COMMIT TRANSACTION;
            RETURN;
        END

        IF @saldo IS NULL OR @saldo < @importe
            THROW 50024, N'Saldo insuficiente para realizar la apuesta.', 1;

        -- Cuota total = producto de las cuotas (se calcula con logaritmos y se limita para no desbordar)
        SELECT @producto = EXP(SUM(LOG(CAST(cuota AS FLOAT)))) FROM @selecciones;
        SET @cuota_total = CAST(ROUND(IIF(@producto > 99999999, 99999999, @producto), 4) AS DECIMAL(18,4));
        SET @posible_ganancia = ROUND(@importe * @cuota_total, 2);
        IF @posible_ganancia > @premio_maximo SET @posible_ganancia = @premio_maximo;   -- premio maximo

        INSERT INTO Apuesta (id_cliente, importe, cuota_total, posible_ganancia, estado, clave_idempotencia)
        VALUES (@id_cliente, @importe, @cuota_total, @posible_ganancia, 'PENDIENTE', @clave);
        SET @id_apuesta = SCOPE_IDENTITY();

        INSERT INTO Apuesta_Detalle (id_apuesta, id_cuota, cuota)
        SELECT @id_apuesta, id_cuota, cuota FROM @selecciones;   -- estado = PENDIENTE por defecto

        UPDATE Saldo SET saldo_actual = saldo_actual - @importe, fecha_actualizacion = SYSDATETIME()
        WHERE id_saldo = @id_saldo;
        INSERT INTO Movimiento_Saldo (id_saldo, id_apuesta, importe, tipo, concepto)
        VALUES (@id_saldo, @id_apuesta, @importe, 'DEBITO',
                CONCAT(IIF(@n = 1, N'Apuesta #', N'Apuesta combinada #'), @id_apuesta, N' registrada'));
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* 4. Permiso de la cuenta de la app sobre el tipo nuevo (EXECUTE sobre el esquema dbo ya cubre el procedimiento) */
GRANT EXECUTE ON TYPE::dbo.tt_SeleccionApuesta TO soccerbet_app;
GO

/* 5. Verificacion: debe mostrar el procedimiento con 9 parametros y la columna nueva */
SELECT COUNT(*) AS parametros_sp FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_RegistrarApuesta');
SELECT COL_LENGTH('dbo.Apuesta','clave_idempotencia') AS columna_clave;
GO

/* PRUEBA OPCIONAL (cambia el 1 por un id_cliente con saldo; luego borra o ignora la apuesta de prueba):
DECLARE @s dbo.tt_SeleccionApuesta, @a BIGINT, @c DECIMAL(18,4), @p DECIMAL(18,2), @r BIT;
INSERT INTO @s VALUES (1, 2.10), (2, 1.80);
EXEC dbo.sp_RegistrarApuesta 1, @s, 5000, N'prueba-001', 20000000, @a OUT, @c OUT, @p OUT, @r OUT;
SELECT @a AS id, @c AS cuota_total, @p AS ganancia, @r AS repetida;   -- 3.7800 y 18900.00
EXEC dbo.sp_RegistrarApuesta 1, @s, 5000, N'prueba-001', 20000000, @a OUT, @c OUT, @p OUT, @r OUT;
SELECT @a AS id, @r AS repetida;                                        -- misma apuesta, repetida = 1, saldo no vuelve a bajar
*/
