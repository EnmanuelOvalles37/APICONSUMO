// Servicios/ClienteService.cs
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Dapper;

namespace Consumo_App.Servicios
{
    public class ClienteService : IClienteService
    {
        private readonly SqlConnectionFactory _db;

        public ClienteService(SqlConnectionFactory db)
        {
            _db = db;
        }

        public async Task<IEnumerable<ClienteDto>> GetAllClientesAsync()
        {
            using var conn = _db.Create();
            return await conn.QueryAsync<ClienteDto>(@"
                SELECT Id, EmpresaId, Codigo, Nombre, Cedula, Grupo, Saldo, SaldoOriginal, Activo
                FROM Clientes
                WHERE Activo = 1
                ORDER BY Nombre");
        }

        public async Task<ClienteDto?> GetClienteByIdAsync(int id)
        {
            using var conn = _db.Create();
            return await conn.QueryFirstOrDefaultAsync<ClienteDto>(@"
                SELECT Id, EmpresaId, Codigo, Nombre, Cedula, Grupo, Saldo, SaldoOriginal, Activo
                FROM Clientes
                WHERE Id = @Id AND Activo = 1",
                new { Id = id });
        }

        public async Task<IEnumerable<ClienteDto>> GetClientesByGrupoAsync(string grupo)
        {
            using var conn = _db.Create();
            return await conn.QueryAsync<ClienteDto>(@"
                SELECT Id, EmpresaId, Codigo, Nombre, Cedula, Grupo, Saldo, SaldoOriginal, Activo
                FROM Clientes
                WHERE Grupo = @Grupo AND Activo = 1
                ORDER BY Nombre",
                new { Grupo = grupo });
        }

        public async Task<IEnumerable<string>> GetGruposAsync()
        {
            using var conn = _db.Create();
            return await conn.QueryAsync<string>(@"
                SELECT DISTINCT Grupo
                FROM Clientes
                WHERE Activo = 1
                ORDER BY Grupo");
        }

        public async Task<int> CreateClienteAsync(ClienteCreateDto dto)
        {
            using var conn = _db.Create();
            return await conn.QuerySingleAsync<int>(@"
                INSERT INTO Clientes (EmpresaId, Codigo, Nombre, Cedula, Grupo, SaldoOriginal, Saldo, Activo)
                OUTPUT INSERTED.Id
                VALUES (@EmpresaId, @Codigo, @Nombre, @Cedula, @Grupo, @SaldoOriginal, @SaldoOriginal, 1)",
                dto);
        }

        public async Task<bool> UpdateClienteAsync(int id, ClienteUpdateDto dto)
        {
            using var conn = _db.Create();

            // Obtener cliente actual
            var cliente = await conn.QueryFirstOrDefaultAsync<ClienteDto>(
                "SELECT * FROM Clientes WHERE Id = @Id",
                new { Id = id });

            if (cliente == null) return false;

            var rows = await conn.ExecuteAsync(@"
                UPDATE Clientes SET
                    Codigo = COALESCE(@Codigo, Codigo),
                    Nombre = COALESCE(@Nombre, Nombre),
                    Cedula = COALESCE(@Cedula, Cedula),
                    Grupo = COALESCE(@Grupo, Grupo),
                    SaldoOriginal = COALESCE(@SaldoOriginal, SaldoOriginal),
                    Activo = COALESCE(@Activo, Activo)
                WHERE Id = @Id",
                new
                {
                    Id = id,
                    dto.Codigo,
                    dto.Nombre,
                    dto.Cedula,
                    dto.Grupo,
                    dto.SaldoOriginal,
                    dto.Activo
                });

            return rows > 0;
        }

        public async Task<bool> DeleteClienteAsync(int id)
        {
            using var conn = _db.Create();
            var rows = await conn.ExecuteAsync(
                "UPDATE Clientes SET Activo = 0 WHERE Id = @Id",
                new { Id = id });
            return rows > 0;
        }

        public async Task<bool> ClienteExistsAsync(int id)
        {
            using var conn = _db.Create();
            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Clientes WHERE Id = @Id AND Activo = 1",
                new { Id = id });
            return exists.HasValue;
        }

        public async Task<bool> UpdateSaldoAsync(int clienteId, decimal monto)
        {
            using var conn = _db.Create();
            var rows = await conn.ExecuteAsync(
                "UPDATE Clientes SET Saldo = Saldo + @Monto WHERE Id = @Id AND Activo = 1",
                new { Id = clienteId, Monto = monto });
            return rows > 0;
        }

        public async Task<IReadOnlyList<ClienteDtos>> GetAllClientesDtoAsync()
        {
            using var conn = _db.Create();
            var result = await conn.QueryAsync<ClienteDtos>(@"
                SELECT 
                    c.Id,
                    c.Nombre,
                    c.Grupo,
                    c.Saldo,
                    c.SaldoOriginal,
                    c.Activo,
                    e.Nombre AS EmpresaNombre,
                    e.Rnc AS EmpresaRnc
                FROM Clientes c
                LEFT JOIN Empresas e ON c.EmpresaId = e.Id
                ORDER BY c.Nombre");

            return result.ToList();
        }

        public async Task<ClienteDtos?> GetClienteDtoByIdAsync(int id)
        {
            using var conn = _db.Create();
            return await conn.QueryFirstOrDefaultAsync<ClienteDtos>(@"
                SELECT 
                    c.Id,
                    c.Nombre,
                    c.Grupo,
                    c.Saldo,
                    c.SaldoOriginal,
                    c.Activo,
                    e.Nombre AS EmpresaNombre,
                    e.Rnc AS EmpresaRnc
                FROM Clientes c
                LEFT JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.Id = @Id",
                new { Id = id });
        }
    }

    // DTO para las operaciones del servicio
    public class ClienteDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
        public string Grupo { get; set; } = "";
        public decimal Saldo { get; set; }
        public decimal SaldoOriginal { get; set; }
        public bool Activo { get; set; }
    }
}