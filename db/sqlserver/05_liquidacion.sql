/* =====================================================================
   SoccerBet - Liquidacion de sencillas y combinadas, con selecciones anuladas (RF14)
   Ejecutar UNA VEZ como administrador sobre CasaApuestas (despues del 03 y el 04). Se puede repetir.
   La app los usara en la fase 6 (simulador). La firma de sp_ResolverApuestasPartido NO cambia.
   ===================================================================== */
USE [CasaApuestas];
GO

IF TYPE_ID('dbo.tt_CuotaAnulada') IS NULL
    CREATE TYPE dbo.tt_CuotaAnulada AS TABLE (id_cuota INT NOT NULL PRIMARY KEY);
GO

/* ---------------------------------------------------------------------
   Nucleo (uso interno): liquida TODAS las apuestas PENDIENTE que ya se puedan decidir.
   - PERDIDA : alguna seleccion PERDIDA (aunque otras sigan pendientes).
   - ANULADA : todas las selecciones ANULADAS -> se devuelve el importe.
   - GANADA  : ninguna pendiente ni perdida y al menos una GANADA. Las ANULADAS cuentan como cuota 1.00,
               asi que el premio = importe x producto de las cuotas GANADAS, sin superar posible_ganancia (tope).
   Es idempotente: solo toca apuestas PENDIENTE, por eso sirve tambien para reconciliar.
   Debe llamarse DENTRO de una transaccion abierta por quien la invoca.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.sp_LiquidarApuestas
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ahora DATETIME2 = SYSDATETIME();
    DECLARE @pagos TABLE (id_apuesta BIGINT PRIMARY KEY, id_cliente INT, monto DECIMAL(18,2), concepto NVARCHAR(250));
    DECLARE @perdidas INT, @anuladas INT, @ganadas INT;

    -- 1) Perdidas
    UPDATE a SET a.estado = 'PERDIDA', a.fecha_resolucion = @ahora
    FROM Apuesta a
    WHERE a.estado = 'PENDIENTE'
      AND EXISTS (SELECT 1 FROM Apuesta_Detalle d WHERE d.id_apuesta = a.id_apuesta AND d.estado = 'PERDIDA');
    SET @perdidas = @@ROWCOUNT;

    -- 2) Anuladas por completo: devolver el importe
    UPDATE a SET a.estado = 'ANULADA', a.fecha_resolucion = @ahora
    OUTPUT inserted.id_apuesta, inserted.id_cliente, inserted.importe,
           CONCAT(N'Devolución de la apuesta #', inserted.id_apuesta) INTO @pagos
    FROM Apuesta a
    WHERE a.estado = 'PENDIENTE'
      AND NOT EXISTS (SELECT 1 FROM Apuesta_Detalle d WHERE d.id_apuesta = a.id_apuesta AND d.estado <> 'ANULADA');
    SET @anuladas = @@ROWCOUNT;

    -- 3) Ganadas: premio recalculado sin las selecciones anuladas
    ;WITH prod AS (
        SELECT a.id_apuesta,
               ROUND(a.importe * EXP(SUM(LOG(CAST(d.cuota AS FLOAT)))), 2) AS premio_calc
        FROM Apuesta a
        INNER JOIN Apuesta_Detalle d ON d.id_apuesta = a.id_apuesta AND d.estado = 'GANADA'
        WHERE a.estado = 'PENDIENTE'
          AND NOT EXISTS (SELECT 1 FROM Apuesta_Detalle x WHERE x.id_apuesta = a.id_apuesta AND x.estado IN ('PENDIENTE','PERDIDA'))
        GROUP BY a.id_apuesta, a.importe
    )
    UPDATE a SET a.estado = 'GANADA', a.fecha_resolucion = @ahora
    OUTPUT inserted.id_apuesta, inserted.id_cliente,
           CAST(IIF(p.premio_calc < inserted.posible_ganancia, p.premio_calc, inserted.posible_ganancia) AS DECIMAL(18,2)),
           CONCAT(N'Premio de la apuesta #', inserted.id_apuesta) INTO @pagos
    FROM Apuesta a INNER JOIN prod p ON p.id_apuesta = a.id_apuesta;
    SET @ganadas = @@ROWCOUNT;

    -- 4) Acreditar (premios y devoluciones)
    IF EXISTS (SELECT 1 FROM @pagos)
    BEGIN
        UPDATE s SET s.saldo_actual = s.saldo_actual + g.total, s.fecha_actualizacion = @ahora
        FROM Saldo s
        INNER JOIN (SELECT id_cliente, SUM(monto) AS total FROM @pagos GROUP BY id_cliente) g ON g.id_cliente = s.id_cliente;
        INSERT INTO Movimiento_Saldo (id_saldo, id_apuesta, importe, tipo, concepto)
        SELECT s.id_saldo, p.id_apuesta, p.monto, 'CREDITO', p.concepto
        FROM @pagos p INNER JOIN Saldo s ON s.id_cliente = p.id_cliente;
    END

    SELECT @ganadas AS apuestas_ganadas, @perdidas AS apuestas_perdidas, @anuladas AS apuestas_anuladas;
END;
GO

/* ---------------------------------------------------------------------
   Finalizar partido: la app envia que cuotas ganaron / perdieron (igual que antes)
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.sp_ResolverApuestasPartido
    @resultados dbo.tt_ResultadoCuota READONLY
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE d SET d.estado = IIF(r.ganadora = 1, 'GANADA', 'PERDIDA')
        FROM Apuesta_Detalle d
        INNER JOIN @resultados r ON r.id_cuota = d.id_cuota
        INNER JOIN Apuesta a ON a.id_apuesta = d.id_apuesta
        WHERE a.estado = 'PENDIENTE' AND d.estado = 'PENDIENTE';

        EXEC dbo.sp_LiquidarApuestas;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ---------------------------------------------------------------------
   Anular partido: la app envia TODAS las cuotas del partido. Esas selecciones salen de las combinadas
   (cuota 1.00); las sencillas y las combinadas totalmente anuladas se devuelven.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.sp_AnularApuestasPartido
    @cuotas dbo.tt_CuotaAnulada READONLY
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE d SET d.estado = 'ANULADA'
        FROM Apuesta_Detalle d
        INNER JOIN @cuotas c ON c.id_cuota = d.id_cuota
        INNER JOIN Apuesta a ON a.id_apuesta = d.id_apuesta
        WHERE a.estado = 'PENDIENTE' AND d.estado = 'PENDIENTE';

        EXEC dbo.sp_LiquidarApuestas;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON TYPE::dbo.tt_CuotaAnulada TO soccerbet_app;
GO

/* Verificacion: deben aparecer los 3 procedimientos */
SELECT name FROM sys.procedures WHERE name IN ('sp_LiquidarApuestas','sp_ResolverApuestasPartido','sp_AnularApuestasPartido');
GO
