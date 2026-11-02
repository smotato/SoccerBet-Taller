-- SoccerBet - PostgreSQL 16: partidos y cuotas (Calendario, Partido, Mercado, Cuota)
-- Hora: se guarda la hora local de Colombia (timestamp sin zona).

CREATE TABLE calendario (
  id_calendario  INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  id_liga        INT         NOT NULL,                 -- ID logico -> MySQL.liga
  numero_jornada INT         NOT NULL CHECK (numero_jornada > 0),
  temporada      VARCHAR(50) NOT NULL,
  fecha_inicio   DATE NULL,
  fecha_fin      DATE NULL,
  estado         VARCHAR(20) NOT NULL DEFAULT 'PROGRAMADA'
                 CHECK (estado IN ('PROGRAMADA','EN_CURSO','FINALIZADA')),
  UNIQUE (id_liga, temporada, numero_jornada)
);

CREATE TABLE partido (
  id_partido          INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  id_calendario       INT NOT NULL REFERENCES calendario(id_calendario),
  id_estadio          INT NOT NULL,                    -- ID logico -> MySQL.estadio
  id_equipo_local     INT NOT NULL,                    -- ID logico -> MySQL.equipo
  id_equipo_visitante INT NOT NULL,                    -- ID logico -> MySQL.equipo
  fecha_hora          TIMESTAMP NOT NULL,
  goles_local         INT NULL CHECK (goles_local >= 0),
  goles_visitante     INT NULL CHECK (goles_visitante >= 0),
  estado              VARCHAR(30) NOT NULL DEFAULT 'PROGRAMADO'
                      CHECK (estado IN ('PROGRAMADO','EN_VIVO','FINALIZADO','SUSPENDIDO')),
  fecha_finalizacion  TIMESTAMP NULL,
  CHECK (id_equipo_local <> id_equipo_visitante)
);
CREATE INDEX ix_partido_fecha  ON partido(fecha_hora);
CREATE INDEX ix_partido_estado ON partido(estado);

CREATE TABLE mercado (
  id_mercado  INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  nombre      VARCHAR(150) NOT NULL UNIQUE,
  descripcion VARCHAR(500) NULL,
  activo      BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE cuota (
  id_cuota         INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  id_partido       INT NOT NULL REFERENCES partido(id_partido),
  id_mercado       INT NOT NULL REFERENCES mercado(id_mercado),
  seleccion        VARCHAR(150) NOT NULL,              -- texto para mostrar: 'Gana Junior', 'Empate'...
  codigo_seleccion VARCHAR(20)  NOT NULL               -- codigo estable para liquidar sin depender de nombres
                   CHECK (codigo_seleccion IN ('LOCAL','EMPATE','VISITANTE','MAS_2_5','MENOS_2_5','SI','NO')),
  cuota            NUMERIC(10,4) NOT NULL CHECK (cuota > 1),
  disponible       BOOLEAN NOT NULL DEFAULT TRUE,
  UNIQUE (id_partido, id_mercado, seleccion)
);
CREATE INDEX ix_cuota_partido ON cuota(id_partido);

-- Mercados iniciales (mismos ids que usaba fn_CuotaGanadora)
INSERT INTO mercado (nombre, descripcion) VALUES
 ('Resultado final 1X2',   'Gana el local, empate o gana el visitante'),
 ('Total de goles 2.5',    'Mas o menos de 2.5 goles en el partido'),
 ('Ambos equipos anotan',  'Si ambos equipos marcan al menos un gol');

-- Vista para el home: partidos futuros/en vivo con sus cuotas disponibles
CREATE VIEW vw_cuotas_disponibles AS
SELECT p.id_partido, p.fecha_hora, p.estado AS estado_partido, p.id_equipo_local, p.id_equipo_visitante,
       p.goles_local, p.goles_visitante, m.id_mercado, m.nombre AS mercado,
       c.id_cuota, c.seleccion, c.codigo_seleccion, c.cuota
FROM partido p
JOIN cuota c   ON c.id_partido = p.id_partido AND c.disponible
JOIN mercado m ON m.id_mercado = c.id_mercado AND m.activo
WHERE p.estado IN ('PROGRAMADO','EN_VIVO');

-- Reemplaza fn_CuotaGanadora: NULL = no se puede determinar (partido sin finalizar)
CREATE OR REPLACE FUNCTION fn_cuota_ganadora(p_id_cuota INT) RETURNS BOOLEAN
LANGUAGE sql STABLE AS $$
  SELECT CASE c.codigo_seleccion
           WHEN 'LOCAL'     THEN p.goles_local > p.goles_visitante
           WHEN 'EMPATE'    THEN p.goles_local = p.goles_visitante
           WHEN 'VISITANTE' THEN p.goles_visitante > p.goles_local
           WHEN 'MAS_2_5'   THEN p.goles_local + p.goles_visitante > 2
           WHEN 'MENOS_2_5' THEN p.goles_local + p.goles_visitante < 3
           WHEN 'SI'        THEN p.goles_local > 0 AND p.goles_visitante > 0
           WHEN 'NO'        THEN p.goles_local = 0 OR p.goles_visitante = 0
         END
  FROM cuota c JOIN partido p ON p.id_partido = c.id_partido
  WHERE c.id_cuota = p_id_cuota AND p.estado = 'FINALIZADO'
    AND p.goles_local IS NOT NULL AND p.goles_visitante IS NOT NULL;
$$;

-- Reemplaza la parte de partido de sp_RegistrarResultadoPartido.
-- Devuelve (id_cuota, ganadora): la app lo envia a SQL Server -> sp_ResolverApuestasPartido.
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
                     estado = 'FINALIZADO', fecha_finalizacion = now()
  WHERE partido.id_partido = p_id_partido;

  UPDATE cuota SET disponible = FALSE WHERE cuota.id_partido = p_id_partido;

  UPDATE calendario cal SET estado = 'FINALIZADA'
  FROM partido p
  WHERE p.id_calendario = cal.id_calendario AND p.id_partido = p_id_partido
    AND NOT EXISTS (SELECT 1 FROM partido x WHERE x.id_calendario = cal.id_calendario AND x.estado <> 'FINALIZADO');

  RETURN QUERY SELECT c.id_cuota, COALESCE(fn_cuota_ganadora(c.id_cuota), FALSE)
               FROM cuota c WHERE c.id_partido = p_id_partido;
END $$;
