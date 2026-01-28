using Dapper;
using Consumo_App.Data.Sql;

namespace Consumo_App.Data
{
    public static class SeedData
    {
        public static async Task SeedAsync(SqlConnectionFactory connectionFactory)
        {
            using var connection = connectionFactory.Create();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                // =========== EMPRESA ===========
                var empresaId = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT Id FROM Empresas WHERE Rnc = '101010101'", transaction: transaction);

                if (!empresaId.HasValue)
                {
                    empresaId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO Empresas (Nombre, Rnc, Direccion, Telefono, LimiteCredito, DiaCorte, Activo, CreatedAt)
                        OUTPUT INSERTED.Id
                        VALUES (@Nombre, @Rnc, @Direccion, @Telefono, @LimiteCredito, @DiaCorte, 1, @CreatedAt)",
                        new
                        {
                            Nombre = "Farmacia Central",
                            Rnc = "101010101",
                            Direccion = "Av. Duarte #100",
                            Telefono = "8095550000",
                            LimiteCredito = 10000m,
                            DiaCorte = 15,
                            CreatedAt = DateTime.UtcNow
                        }, transaction);
                }

                // =========== CLIENTES ===========
                async Task EnsureCliente(string cedula, string nombre, string codigo, string grupo)
                {
                    var existe = await connection.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(1) FROM Clientes WHERE Cedula = @Cedula OR Codigo = @Codigo",
                        new { Cedula = cedula, Codigo = codigo }, transaction) > 0;

                    if (!existe)
                    {
                        await connection.ExecuteAsync(@"
                            INSERT INTO Clientes (Cedula, Nombre, EmpresaId, Codigo, Grupo, Saldo, Activo)
                            VALUES (@Cedula, @Nombre, @EmpresaId, @Codigo, @Grupo, 0, 1)",
                            new
                            {
                                Cedula = cedula,
                                Nombre = nombre,
                                EmpresaId = empresaId.Value,
                                Codigo = codigo,
                                Grupo = grupo
                            }, transaction);
                    }
                }

                await EnsureCliente("00100000001", "Juan Pérez", "CLI001", "Grupo 1");
                await EnsureCliente("00100000002", "María López", "CLI002", "Grupo 1");

                // =========== PROVEEDOR ===========
                var proveedorId = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT Id FROM Proveedores WHERE Nombre = 'Colmado La Esquina'", transaction: transaction);

                if (!proveedorId.HasValue)
                {
                    proveedorId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO Proveedores (Nombre, Rnc, Direccion, Telefono, Email, Contacto, DiasCorte, PorcentajeComision, Activo, CreadoUtc)
                        OUTPUT INSERTED.Id
                        VALUES (@Nombre, @Rnc, @Direccion, @Telefono, @Email, @Contacto, @DiasCorte, @PorcentajeComision, 1, @CreadoUtc)",
                        new
                        {
                            Nombre = "Colmado La Esquina",
                            Rnc = "123454321",
                            Direccion = "calle primera 7",
                            Telefono = "809-123-6543",
                            Email = "laesquina@gmail.com",
                            Contacto = "Carlos Pimentel",
                            DiasCorte = 15,
                            PorcentajeComision = 2m,
                            CreadoUtc = DateTime.UtcNow
                        }, transaction);
                }

                // =========== TIENDA ===========
                var tiendaId = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT Id FROM ProveedorTiendas WHERE ProveedorId = @ProveedorId AND Nombre = 'Sucursal Principal'",
                    new { ProveedorId = proveedorId.Value }, transaction);

                if (!tiendaId.HasValue)
                {
                    tiendaId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO ProveedorTiendas (ProveedorId, Nombre, Activo)
                        OUTPUT INSERTED.Id
                        VALUES (@ProveedorId, @Nombre, 1)",
                        new { ProveedorId = proveedorId.Value, Nombre = "Sucursal Principal" }, transaction);
                }

                // =========== CAJA ===========
                var cajaId = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT Id FROM ProveedorCajas WHERE TiendaId = @TiendaId AND Nombre = 'Caja 1'",
                    new { TiendaId = tiendaId.Value }, transaction);

                if (!cajaId.HasValue)
                {
                    cajaId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO ProveedorCajas (TiendaId, Nombre, Activo)
                        OUTPUT INSERTED.Id
                        VALUES (@TiendaId, @Nombre, 1)",
                        new { TiendaId = tiendaId.Value, Nombre = "Caja 1" }, transaction);
                }

                // =========== USUARIOS ===========
                async Task<int> EnsureUsuario(string user)
                {
                    // Obtener rol "usuario"
                    var rolId = await connection.ExecuteScalarAsync<int?>(@"
                        SELECT Id FROM Roles WHERE Nombre = 'usuario'", transaction: transaction);

                    if (!rolId.HasValue)
                    {
                        rolId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO Roles (Nombre, Descripcion) 
                            OUTPUT INSERTED.Id 
                            VALUES ('usuario', 'Usuario estándar')", transaction: transaction);
                    }

                    // Verificar si existe el usuario
                    var usuarioId = await connection.ExecuteScalarAsync<int?>(@"
                        SELECT Id FROM Usuarios WHERE Nombre = @Nombre",
                        new { Nombre = user }, transaction);

                    if (!usuarioId.HasValue)
                    {
                        usuarioId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO Usuarios (Nombre, Contrasena, RolId, Activo, CreadoUtc)
                            OUTPUT INSERTED.Id
                            VALUES (@Nombre, @Contrasena, @RolId, 1, @CreadoUtc)",
                            new
                            {
                                Nombre = user,
                                Contrasena = "1234", // Nota: En producción debería estar hasheado
                                RolId = rolId.Value,
                                CreadoUtc = DateTime.UtcNow
                            }, transaction);
                    }

                    return usuarioId.Value;
                }

                var cajeroId = await EnsureUsuario("cajero1");

                // =========== ASIGNACIÓN ===========
                var asignacionExiste = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1) FROM ProveedorAsignaciones 
                    WHERE ProveedorId = @ProveedorId AND UsuarioId = @UsuarioId 
                      AND TiendaId = @TiendaId AND CajaId = @CajaId",
                    new
                    {
                        ProveedorId = proveedorId.Value,
                        UsuarioId = cajeroId,
                        TiendaId = tiendaId.Value,
                        CajaId = cajaId.Value
                    }, transaction) > 0;

                if (!asignacionExiste)
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO ProveedorAsignaciones (ProveedorId, UsuarioId, TiendaId, CajaId, Rol, Activo)
                        VALUES (@ProveedorId, @UsuarioId, @TiendaId, @CajaId, @Rol, 1)",
                        new
                        {
                            ProveedorId = proveedorId.Value,
                            UsuarioId = cajeroId,
                            TiendaId = tiendaId.Value,
                            CajaId = cajaId.Value,
                            Rol = "Cajero"
                        }, transaction);
                }

                // =========== CONSUMO DE PRUEBA ===========
                var hayConsumos = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM Consumos", transaction: transaction) > 0;

                if (!hayConsumos)
                {
                    var clienteId = await connection.ExecuteScalarAsync<int>(
                        "SELECT TOP 1 Id FROM Clientes", transaction: transaction);

                    await connection.ExecuteAsync(@"
                        INSERT INTO Consumos 
                            (Fecha, ClienteId, EmpresaId, ProveedorId, TiendaId, CajaId, Monto, Nota, UsuarioRegistradorId, Reversado)
                        VALUES 
                            (@Fecha, @ClienteId, @EmpresaId, @ProveedorId, @TiendaId, @CajaId, @Monto, @Nota, @UsuarioId, 0)",
                        new
                        {
                            Fecha = DateTime.UtcNow,
                            ClienteId = clienteId,
                            EmpresaId = empresaId.Value,
                            ProveedorId = proveedorId.Value,
                            TiendaId = tiendaId.Value,
                            CajaId = cajaId.Value,
                            Monto = 250.00m,
                            Nota = "Compra de prueba",
                            UsuarioId = cajeroId
                        }, transaction);
                }

                transaction.Commit();
                Console.WriteLine("✓ SeedData ejecutado correctamente.");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public static class SeedDataExtensions
    {
        /// <summary>
        /// Extension method para usar en Program.cs: await app.SeedDataAsync();
        /// </summary>
        public static async Task SeedDataAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>();
            await SeedData.SeedAsync(factory);
        }
    }
}