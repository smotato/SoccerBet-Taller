-- SoccerBet - Fase 6: ajustes de PostgreSQL para partidos en vivo y anulacion de partidos.
-- Ejecutar UNA VEZ en tu BD ya creada (se puede repetir sin romper nada). Como superusuario postgres.

-- 1. Minuto simulado del partido y cuota inicial (la base para recalcular cuotas en vivo)
ALTER TABLE partido ADD COLUMN IF NOT EXISTS minuto INT NULL CHECK (minuto BETWEEN 0 AND 130);
ALTER TABLE cuota   ADD COLUMN IF NOT EXISTS cuota_inicial NUMERIC(10,4) NULL;
UPDATE cuota SET cuota_inicial = cuota WHERE cuota_inicial IS NULL;
-- Los partidos ya finalizados terminan en el minuto 90
UPDATE partido SET minuto = 90 WHERE estado = 'FINALIZADO' AND minuto IS NULL;

-- 2. Finalizar partido: igual que antes, pero deja minuto = 90 y una jornada se cierra
--    cuando todos sus partidos estan FINALIZADO o SUSPENDIDO (un partido anulado no la deja abierta).
CREATE OR REPLACE FUNCTION registrar_resultado_partido(p_id_partido INT, p_goles_local INT, p_goles_visitante INT)
RETURNS TABLE (id_cuota INT, ganadora BOOLEAN)
LANGUAGE plpgsql AS $$
BEGIN
  IF p_goles_local < 0 OR p_goles_visitante < 0 THEN
    RAISE EXCEPTION 'Los goles no pueden ser negativos.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM partido WHERE partido.id_partido = p_id_partido
                   AND partido.estado IN ('PROGRAMADO','EN_VIVO')) THEN
    RAISE EXCEPTION 'El partido no existe o ya fue finalizado.';
  END IF;

  UPDATE partido SET goles_local = p_goles_local, goles_visitante = p_goles_visitante,
                     estado = 'FINALIZADO', minuto = 90, fecha_finalizacion = now()
  WHERE partido.id_partido = p_id_partido;

  UPDATE cuota SET disponible = FALSE WHERE cuota.id_partido = p_id_partido;

  UPDATE calendario cal SET estado = 'FINALIZADA'
  FROM partido p
  WHERE p.id_calendario = cal.id_calendario AND p.id_partido = p_id_partido
    AND NOT EXISTS (SELECT 1 FROM partido x WHERE x.id_calendario = cal.id_calendario
                    AND x.estado NOT IN ('FINALIZADO','SUSPENDIDO'));

  RETURN QUERY SELECT c.id_cuota, COALESCE(fn_cuota_ganadora(c.id_cuota), FALSE)
               FROM cuota c WHERE c.id_partido = p_id_partido;
END $$;

-- 3. Anular partido (modo demostracion): deja el partido SUSPENDIDO, cierra todas sus cuotas
--    y devuelve los id_cuota para que la app anule las selecciones en SQL Server (sp_AnularApuestasPartido).
CREATE OR REPLACE FUNCTION anular_partido(p_id_partido INT)
RETURNS TABLE (id_cuota INT)
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM partido WHERE partido.id_partido = p_id_partido
                   AND partido.estado IN ('PROGRAMADO','EN_VIVO')) THEN
    RAISE EXCEPTION 'El partido no existe o ya fue finalizado o anulado.';
  END IF;

  UPDATE partido SET estado = 'SUSPENDIDO', fecha_finalizacion = now()
  WHERE partido.id_partido = p_id_partido;

  UPDATE cuota SET disponible = FALSE WHERE cuota.id_partido = p_id_partido;

  UPDATE calendario cal SET estado = 'FINALIZADA'
  FROM partido p
  WHERE p.id_calendario = cal.id_calendario AND p.id_partido = p_id_partido
    AND NOT EXISTS (SELECT 1 FROM partido x WHERE x.id_calendario = cal.id_calendario
                    AND x.estado NOT IN ('FINALIZADO','SUSPENDIDO'));

  RETURN QUERY SELECT c.id_cuota FROM cuota c WHERE c.id_partido = p_id_partido;
END $$;

-- 4. Permisos de la cuenta de la app sobre la funcion nueva (si el usuario existe en tu BD)
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'soccerbet_app') THEN
    GRANT EXECUTE ON FUNCTION anular_partido(INT) TO soccerbet_app;
    GRANT EXECUTE ON FUNCTION registrar_resultado_partido(INT, INT, INT) TO soccerbet_app;
  END IF;
END $$;

-- Verificacion: debe mostrar 2 columnas nuevas y las 2 funciones
SELECT column_name FROM information_schema.columns
 WHERE (table_name = 'partido' AND column_name = 'minuto') OR (table_name = 'cuota' AND column_name = 'cuota_inicial');
SELECT proname FROM pg_proc WHERE proname IN ('anular_partido', 'registrar_resultado_partido');
