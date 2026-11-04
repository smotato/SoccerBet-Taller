-- SoccerBet - Fase 6: la fecha de finalizacion se guarda en hora local de Colombia (la BD corre en UTC).
-- Ejecutar UNA VEZ como superusuario (se puede repetir). Reemplaza las dos funciones del script 04.

-- Finalizar partido: igual que antes, pero deja minuto = 90 y una jornada se cierra
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
                     estado = 'FINALIZADO', minuto = 90, fecha_finalizacion = (now() AT TIME ZONE 'America/Bogota')
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

-- Anular partido (modo demostracion): deja el partido SUSPENDIDO, cierra todas sus cuotas
--    y devuelve los id_cuota para que la app anule las selecciones en SQL Server (sp_AnularApuestasPartido).
CREATE OR REPLACE FUNCTION anular_partido(p_id_partido INT)
RETURNS TABLE (id_cuota INT)
LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM partido WHERE partido.id_partido = p_id_partido
                   AND partido.estado IN ('PROGRAMADO','EN_VIVO')) THEN
    RAISE EXCEPTION 'El partido no existe o ya fue finalizado o anulado.';
  END IF;

  UPDATE partido SET estado = 'SUSPENDIDO', fecha_finalizacion = (now() AT TIME ZONE 'America/Bogota')
  WHERE partido.id_partido = p_id_partido;

  UPDATE cuota SET disponible = FALSE WHERE cuota.id_partido = p_id_partido;

  UPDATE calendario cal SET estado = 'FINALIZADA'
  FROM partido p
  WHERE p.id_calendario = cal.id_calendario AND p.id_partido = p_id_partido
    AND NOT EXISTS (SELECT 1 FROM partido x WHERE x.id_calendario = cal.id_calendario
                    AND x.estado NOT IN ('FINALIZADO','SUSPENDIDO'));

  RETURN QUERY SELECT c.id_cuota FROM cuota c WHERE c.id_partido = p_id_partido;
END $$;


-- Verificacion: ninguna de las dos funciones debe seguir usando now() sin convertir
SELECT proname, prosrc LIKE '%America/Bogota%' AS usa_hora_colombia
FROM pg_proc WHERE proname IN ('anular_partido', 'registrar_resultado_partido');
