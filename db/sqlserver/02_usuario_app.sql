-- SoccerBet - SQL Server: usuario de la aplicacion con minimos permisos (reemplaza el uso de 'sa').
-- Ejecutar UNA vez en SSMS, conectado con Autenticacion de Windows (o como sa por ultima vez).
-- ANTES de ejecutar: cambia 'CAMBIA_ESTA_CLAVE' por tu contrasena (guardala solo en user-secrets, no en Git).
-- Evita ; = ' " en la contrasena para no romper la cadena de conexion.
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'soccerbet_app')
    CREATE LOGIN [soccerbet_app] WITH PASSWORD = N'CAMBIA_ESTA_CLAVE',
        DEFAULT_DATABASE = [CasaApuestas], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
GO
USE [CasaApuestas];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'soccerbet_app')
    CREATE USER [soccerbet_app] FOR LOGIN [soccerbet_app];
GO
-- Leer tablas y vistas (Cliente, Saldo, Movimiento_Saldo, Apuesta, vistas, etc.)
ALTER ROLE db_datareader ADD MEMBER [soccerbet_app];
-- Unica escritura directa permitida: bloqueo/intentos del login
GRANT UPDATE ON dbo.[Login] TO [soccerbet_app];
-- Todo el dinero y los registros pasan por procedimientos almacenados
GRANT EXECUTE ON SCHEMA::dbo TO [soccerbet_app];
-- Tipo de tabla usado por sp_ResolverApuestasPartido (si existe)
IF TYPE_ID(N'dbo.tt_ResultadoCuota') IS NOT NULL
    GRANT EXECUTE ON TYPE::dbo.tt_ResultadoCuota TO [soccerbet_app];
GO
-- Comprobacion: debe listar el usuario y sus permisos
SELECT dp.name AS usuario, p.permission_name, p.state_desc, OBJECT_NAME(p.major_id) AS objeto
FROM sys.database_principals dp
LEFT JOIN sys.database_permissions p ON p.grantee_principal_id = dp.principal_id
WHERE dp.name = N'soccerbet_app';
GO
