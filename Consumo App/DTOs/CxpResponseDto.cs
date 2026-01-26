public record CxpQuery(string Proveedor, DateTime? Desde, DateTime? Hasta, string? Estado, int Page = 1, int PageSize = 10);

public record CxpItemDto(
    int Id, int ProveedorId, string ProveedorNombre, DateTime Fecha,
    string Documento, string Concepto, decimal Monto, decimal Balance, string Estado);

public record CxpResponseDto(
    IEnumerable<CxpItemDto> Data, int Total, int Page, int PageSize,
    decimal SumBalance, decimal SumBalanceAll);
