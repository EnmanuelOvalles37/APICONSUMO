using Consumo_App.Data.Sql;
using Consumo_App.Models;
using Microsoft.Data.SqlClient;

namespace Consumo_App.Servicios.Sql
{
    public class ProveedorAsignacionSqlService
    {
        private readonly SqlConnectionFactory _factory;

        public ProveedorAsignacionSqlService(SqlConnectionFactory factory)
        {
            _factory = factory;
        }

        // ======================================================
        // ASIGNACIÓN ACTIVA (una sola, para login)
        // ======================================================
        public async Task<ProveedorAsignacion?> GetAsignacionActivaPorUsuarioAsync(int usuarioId)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT TOP 1
                    a.Id,
                    a.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    a.TiendaId,
                    t.Nombre AS TiendaNombre,
                    a.CajaId,
                    c.Nombre AS CajaNombre,
                    a.Rol
                FROM ProveedorAsignaciones a
                INNER JOIN Proveedores p ON p.Id = a.ProveedorId
                LEFT JOIN ProveedorTiendas t ON t.Id = a.TiendaId
                LEFT JOIN ProveedorCajas c ON c.Id = a.CajaId
                WHERE a.UsuarioId = @usuarioId
                  AND a.Activo = 1
                ORDER BY 
                    CASE WHEN a.CajaId IS NOT NULL THEN 1 ELSE 0 END DESC,
                    CASE WHEN a.TiendaId IS NOT NULL THEN 1 ELSE 0 END DESC
            ", conn);

            cmd.Parameters.AddWithValue("@usuarioId", usuarioId);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!reader.Read())
                return null;

            return MapAsignacion(reader);
        }

        // ======================================================
        // TODAS LAS ASIGNACIONES ACTIVAS (contexto)
        // ======================================================
        public async Task<List<ProveedorAsignacion>> GetAsignacionesPorUsuarioAsync(int usuarioId)
        {
            var result = new List<ProveedorAsignacion>();

            using var conn = _factory.Create();
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT
                    a.Id,
                    a.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    a.TiendaId,
                    t.Nombre AS TiendaNombre,
                    a.CajaId,
                    c.Nombre AS CajaNombre,
                    a.Rol
                FROM ProveedorAsignaciones a
                INNER JOIN Proveedores p ON p.Id = a.ProveedorId
                LEFT JOIN ProveedorTiendas t ON t.Id = a.TiendaId
                LEFT JOIN ProveedorCajas c ON c.Id = a.CajaId
                WHERE a.UsuarioId = @usuarioId
                  AND a.Activo = 1
            ", conn);

            cmd.Parameters.AddWithValue("@usuarioId", usuarioId);

            using var reader = await cmd.ExecuteReaderAsync();

            while (reader.Read())
            {
                result.Add(MapAsignacion(reader));
            }

            return result;
        }

        // ======================================================
        // MAPEO CENTRALIZADO
        // ======================================================
        private static ProveedorAsignacion MapAsignacion(SqlDataReader reader)
        {
            return new ProveedorAsignacion
            {
                Id = reader.GetInt32(0),
                ProveedorId = reader.GetInt32(1),
                Proveedor = new Proveedor
                {
                    Id = reader.GetInt32(1),
                    Nombre = reader.GetString(2)
                },
                TiendaId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Tienda = reader.IsDBNull(4) ? null : new ProveedorTienda
                {
                    Nombre = reader.GetString(4)
                },
                CajaId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Caja = reader.IsDBNull(6) ? null : new ProveedorCaja
                {
                    Nombre = reader.GetString(6)
                },
                Rol = reader.GetString(7)
            };
        }
    }
}
