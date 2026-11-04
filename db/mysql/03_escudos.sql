/* =====================================================================
   SoccerBet - Escudos de los equipos (RF6). Ejecutar como administrador sobre soccerbet_catalogo. Se puede repetir.
   Los archivos viven en wwwroot/img/escudos/ (solo se guarda la URL relativa, nunca la imagen en la BD).
   Origen: Wikimedia Commons (cada escudo con licencia "dominio publico"; sigue siendo marca registrada del club: uso
   academico, sin fines comerciales). Ver wwwroot/img/escudos/CREDITOS.md.
   ===================================================================== */
USE soccerbet_catalogo;

UPDATE equipo SET escudo_url = '/img/escudos/independiente-medellin.svg' WHERE id_equipo = 5 AND nombre LIKE 'Independiente Medell%';

UPDATE equipo SET escudo_url = '/img/escudos/atletico-nacional.svg' WHERE id_equipo = 1 AND nombre = 'Atlético Nacional';
UPDATE equipo SET escudo_url = '/img/escudos/millonarios.svg' WHERE id_equipo = 2 AND nombre = 'Millonarios FC';
UPDATE equipo SET escudo_url = '/img/escudos/junior-de-barranquilla.svg' WHERE id_equipo = 3 AND nombre = 'Junior de Barranquilla';
UPDATE equipo SET escudo_url = '/img/escudos/deportivo-cali.svg' WHERE id_equipo = 6 AND nombre = 'Deportivo Cali';
UPDATE equipo SET escudo_url = '/img/escudos/cucuta-deportivo.svg' WHERE id_equipo = 7 AND nombre = 'Cúcuta Deportivo';
UPDATE equipo SET escudo_url = '/img/escudos/independiente-santa-fe.svg' WHERE id_equipo = 8 AND nombre = 'Independiente Santa Fe';
UPDATE equipo SET escudo_url = '/img/escudos/internacional-de-bogota.png' WHERE id_equipo = 10 AND nombre = 'Internacional de Bogotá';
UPDATE equipo SET escudo_url = '/img/escudos/deportes-tolima.svg' WHERE id_equipo = 12 AND nombre = 'Deportes Tolima';
UPDATE equipo SET escudo_url = '/img/escudos/once-caldas.svg' WHERE id_equipo = 13 AND nombre = 'Once Caldas';
UPDATE equipo SET escudo_url = '/img/escudos/deportivo-pereira.svg' WHERE id_equipo = 14 AND nombre = 'Deportivo Pereira';
UPDATE equipo SET escudo_url = '/img/escudos/deportivo-pasto.png' WHERE id_equipo = 15 AND nombre = 'Deportivo Pasto';

SELECT id_equipo, nombre, escudo_url FROM equipo WHERE escudo_url IS NOT NULL;
