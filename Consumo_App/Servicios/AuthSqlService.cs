using Consumo_App.Data.Sql;
using Consumo_App.Models;
using Consumo_App.Servicios;
using Microsoft.Data.SqlClient;

namespace Consumo_App.Servicios.Sql
{
    public class AuthSqlService
    {
        private readonly SqlConnectionFactory _factory;
        private readonly IPasswordHasher _passwordHasher;

        public AuthSqlService(
            SqlConnectionFactory factory,
            IPasswordHasher passwordHasher)
        {
            _factory = factory;
            _passwordHasher = passwordHasher;
        }

        public async Task<Usuario?> AuthenticateAsync(string usuario, string contrasena)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT TOP 1
                    u.Id,
                    u.Nombre,
                    u.Contrasena,
                    u.Activo,
                    u.RolId,
                    r.Nombre AS RolNombre
                FROM Usuarios u
                INNER JOIN Roles r ON r.Id = u.RolId
                WHERE u.Nombre = @usuario
                  AND u.Activo = 1
            ", conn);

            cmd.Parameters.AddWithValue("@usuario", usuario);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!reader.Read())
                return null;

            var passwordHash = reader.GetString(reader.GetOrdinal("Contrasena"));

            // 🔐 VALIDACIÓN REAL DE PASSWORD
            if (!_passwordHasher.Verify(contrasena, passwordHash))
                return null;

            return new Usuario
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Nombre = reader.GetString(reader.GetOrdinal("Nombre")),
                Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                RolId = reader.GetInt32(reader.GetOrdinal("RolId")),
                Rol = new Rol
                {
                    Nombre = reader.GetString(reader.GetOrdinal("RolNombre"))
                }
            };
        }

             public async Task<Usuario?> GetUsuarioPorIdAsync(int userId)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT 
                    u.Id,
                    u.Nombre,
                    u.Activo,
                    u.RolId,
                    r.Nombre AS RolNombre
                FROM Usuarios u
                INNER JOIN Roles r ON r.Id = u.RolId
                WHERE u.Id = @id
            ", conn);

            cmd.Parameters.AddWithValue("@id", userId);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!reader.Read())
                return null;

            return new Usuario
            {
                Id = reader.GetInt32(0),
                Nombre = reader.GetString(1),
                Activo = reader.GetBoolean(2),
                RolId = reader.GetInt32(3),
                Rol = new Rol
                {
                    Nombre = reader.GetString(4)
                }
            };
        }
    }
}
    

