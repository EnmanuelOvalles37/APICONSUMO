public record CxcQuery(string? Cliente, DateTime? Desde, DateTime? Hasta, string? Estado, int Page = 1, int PageSize = 10);

public record CxcItemDto(
    int Id, string? ClienteId, string ClienteNombre, DateTime Fecha,
    string Documento, string Concepto, decimal Monto, decimal Balance, string Estado);

public record CxcResponseDto(
    IEnumerable<CxcItemDto> Data, int Total, int Page, int PageSize,
    decimal SumBalance, decimal SumBalanceAll);
