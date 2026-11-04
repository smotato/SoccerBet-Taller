-- Usuario de la aplicacion: solo lectura sobre el catalogo.
-- Docker ejecuta estos scripts en orden alfabetico, solo al crear el volumen.
-- Cambia la contrasena si quieres, y que coincida con ConnectionStrings:MySql en user-secrets.
CREATE USER IF NOT EXISTS 'soccerbet_app'@'%' IDENTIFIED BY 'soccerbetapp';
GRANT SELECT ON soccerbet_catalogo.* TO 'soccerbet_app'@'%';
FLUSH PRIVILEGES;
