-- SoccerBet - MySQL 8: catalogo deportivo (Deporte, Liga, Equipo, Estadio, Equipo_Estadio, Jugador)
CREATE DATABASE IF NOT EXISTS soccerbet_catalogo CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE soccerbet_catalogo;

CREATE TABLE deporte (
  id_deporte  INT AUTO_INCREMENT PRIMARY KEY,
  nombre      VARCHAR(100) NOT NULL UNIQUE,
  descripcion VARCHAR(500) NULL,
  activo      TINYINT(1)   NOT NULL DEFAULT 1
);

CREATE TABLE liga (
  id_liga    INT AUTO_INCREMENT PRIMARY KEY,
  id_empresa INT          NOT NULL COMMENT 'ID logico -> SQL Server.Empresa',
  id_deporte INT          NOT NULL,
  nombre     VARCHAR(150) NOT NULL,
  pais       VARCHAR(100) NOT NULL,
  temporada  VARCHAR(50)  NOT NULL,
  activa     TINYINT(1)   NOT NULL DEFAULT 1,
  logo_url   VARCHAR(500) NULL,
  CONSTRAINT uq_liga UNIQUE (id_deporte, nombre, temporada),
  CONSTRAINT fk_liga_deporte FOREIGN KEY (id_deporte) REFERENCES deporte(id_deporte)
);

CREATE TABLE estadio (
  id_estadio INT AUTO_INCREMENT PRIMARY KEY,
  nombre     VARCHAR(150) NOT NULL,
  ciudad     VARCHAR(100) NOT NULL,
  pais       VARCHAR(100) NOT NULL,
  capacidad  INT NULL,
  CONSTRAINT ck_estadio_capacidad CHECK (capacidad IS NULL OR capacidad > 0)
);

CREATE TABLE equipo (
  id_equipo  INT AUTO_INCREMENT PRIMARY KEY,
  id_liga    INT          NOT NULL,
  nombre     VARCHAR(150) NOT NULL,
  ciudad     VARCHAR(100) NULL,
  activo     TINYINT(1)   NOT NULL DEFAULT 1,
  escudo_url VARCHAR(500) NULL,
  CONSTRAINT uq_equipo_liga_nombre UNIQUE (id_liga, nombre),
  CONSTRAINT fk_equipo_liga FOREIGN KEY (id_liga) REFERENCES liga(id_liga)
);

CREATE TABLE equipo_estadio (
  id_equipo_estadio INT AUTO_INCREMENT PRIMARY KEY,
  id_equipo    INT        NOT NULL,
  id_estadio   INT        NOT NULL,
  es_principal TINYINT(1) NOT NULL DEFAULT 0,
  CONSTRAINT uq_equipo_estadio UNIQUE (id_equipo, id_estadio),
  CONSTRAINT fk_ee_equipo  FOREIGN KEY (id_equipo)  REFERENCES equipo(id_equipo),
  CONSTRAINT fk_ee_estadio FOREIGN KEY (id_estadio) REFERENCES estadio(id_estadio)
);

CREATE TABLE jugador (
  id_jugador       INT AUTO_INCREMENT PRIMARY KEY,
  id_equipo        INT          NOT NULL,
  nombres          VARCHAR(100) NOT NULL,
  apellidos        VARCHAR(100) NOT NULL,
  numero_camiseta  INT          NULL,
  fecha_nacimiento DATE         NULL,
  nacionalidad     VARCHAR(100) NULL,
  posicion         VARCHAR(30)  NULL,
  foto_url         VARCHAR(500) NULL,
  CONSTRAINT ck_jugador_posicion CHECK (posicion IS NULL OR posicion IN ('PORTERO','DEFENSA','MEDIOCAMPISTA','DELANTERO')),
  CONSTRAINT fk_jugador_equipo FOREIGN KEY (id_equipo) REFERENCES equipo(id_equipo)
);
CREATE INDEX ix_jugador_equipo ON jugador(id_equipo);
