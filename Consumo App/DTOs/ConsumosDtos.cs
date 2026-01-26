namespace Consumo_App.DTOs
{
    public record ConsumoCreateDto(
    int EmpresaId,
    int ClienteId,
    int ProveedorId,
    DateTime Fecha,
    decimal Monto,
    string? Concepto,
    string? Referencia
);
    public record ConsumoListDto(
        int Id,
        DateTime Fecha,
        int EmpresaId,
        string ClienteNombre,
        string ProveedorNombre,
        decimal Monto,
        string? Referencia
    );

    // DTOs/CxpDtos.cs
    public record CxpResumenDto(
        int ProveedorId,
        string Proveedor,
        decimal Saldo);
    public record CxpMovimientoDto(
        DateTime Fecha,
        string Tipo,
        string? Descripcion,
        decimal Debe,
        decimal Haber);
    public record PagoProveedorCreateDto(
        int ProveedorId,
        DateTime Fecha,
        decimal Monto,
        string? Metodo,
        string? Referencia);

    public record ConsumoDetailDto(
        int Id,
        int EmpresaId,
        int ClienteId,
        string ClienteNombre,
        int ProveedorId,
        string ProveedorNombre,
        DateTime Fecha,
        decimal Monto,
        string? Concepto
    );


    public class RegistrarConsumoDto
    {
        public int ClienteId { get; set; }
        public int ProveedorId { get; set; }
        public int TiendaId { get; set; }
        public int CajaId { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? Referencia { get; set; }
        public string? Nota { get; set; }
    }
}
