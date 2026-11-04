/* =====================================================================
   SoccerBet - Escudos de los equipos (RF6). Ejecutar como administrador sobre soccerbet_catalogo. Se puede repetir.
   Los archivos viven en wwwroot/img/escudos/ (solo se guarda la URL relativa, nunca la imagen en la BD).
   Origen: Wikimedia Commons (cada escudo con licencia "dominio publico"; sigue siendo marca registrada del club: uso
   academico, sin fines comerciales). Ver wwwroot/img/escudos/CREDITOS.md.
   ===================================================================== */
USE soccerbet_catalogo;

UPDATE equipo SET escudo_url = '/img/escudos/independiente-medellin.svg' WHERE id_equipo = 5 AND nombre LIKE 'Independiente Medell%';

SELECT id_equipo, nombre, escudo_url FROM equipo WHERE escudo_url IS NOT NULL;
