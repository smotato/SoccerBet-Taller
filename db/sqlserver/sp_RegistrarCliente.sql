USE [CasaApuestas];
GO
CREATE   PROCEDURE [dbo].[sp_RegistrarCliente]
    @nombres          NVARCHAR(100),
    @apellidos        NVARCHAR(100),
    @email            NVARCHAR(150),
    @telefono         NVARCHAR(30) = NULL,
    @documento        NVARCHAR(50),
    @fecha_nacimiento DATE,
    @username         NVARCHAR(100),
    @password_hash    NVARCHAR(500),
    @id_cliente       INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @id_usuario INT;

    IF dbo.fn_EsMayorDeEdad(@fecha_nacimiento) = 0
        THROW 50001, N'El cliente debe ser mayor de edad.', 1;

    IF EXISTS (SELECT 1 FROM Usuario WHERE email = @email)
        THROW 50002, N'Ya existe un usuario con ese correo.', 1;

    IF EXISTS (SELECT 1 FROM Cliente WHERE documento = @documento)
        THROW 50003, N'Ya existe un cliente con ese documento.', 1;

    IF EXISTS (SELECT 1 FROM Login WHERE username = @username)
        THROW 50004, N'El nombre de usuario ya está en uso.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Usuario (nombres, apellidos, email, telefono)
        VALUES (@nombres, @apellidos, @email, @telefono);

        SET @id_usuario = SCOPE_IDENTITY();

        INSERT INTO Usuario_Estado (id_usuario)
        VALUES (@id_usuario);

        INSERT INTO Cliente (id_usuario, documento, fecha_nacimiento)
        VALUES (@id_usuario, @documento, @fecha_nacimiento);

        SET @id_cliente = SCOPE_IDENTITY();

        INSERT INTO Login (id_usuario, username, password_hash)
        VALUES (@id_usuario, @username, @password_hash);

        INSERT INTO Saldo (id_cliente)
        VALUES (@id_cliente);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
