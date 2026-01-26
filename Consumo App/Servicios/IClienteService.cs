// Servicios/IClienteService.cs
using Consumo_App.DTOs;

namespace Consumo_App.Servicios
{
    public interface IClienteService
    {
        Task<IEnumerable<ClienteDto>> GetAllClientesAsync();
        Task<ClienteDto?> GetClienteByIdAsync(int id);
        Task<IEnumerable<ClienteDto>> GetClientesByGrupoAsync(string grupo);
        Task<IEnumerable<string>> GetGruposAsync();
        Task<int> CreateClienteAsync(ClienteCreateDto cliente);
        Task<bool> UpdateClienteAsync(int id, ClienteUpdateDto cliente);
        Task<bool> DeleteClienteAsync(int id);
        Task<bool> ClienteExistsAsync(int id);
        Task<bool> UpdateSaldoAsync(int clienteId, decimal monto);
        Task<IReadOnlyList<ClienteDtos>> GetAllClientesDtoAsync();
        Task<ClienteDtos?> GetClienteDtoByIdAsync(int id);
    }
}