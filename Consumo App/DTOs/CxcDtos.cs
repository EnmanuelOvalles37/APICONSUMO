namespace Consumo_App.DTOs
{
    public class CxcDtos
    {
        public int Id { get; set; }
        public string Rnc { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public decimal LimiteCredito { get; set; }
        public decimal TotalCobrar { get; set; }
        public decimal TotalDisponible { get; set; }
    }

    public class PagedResult<T>
    {
        public IReadOnlyList<T> Data { get; set; } = Array.Empty<T>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public decimal? SumTotalCobrar { get; set; }      
        public decimal? SumTotalDisponible { get; set; }
        public string? EmpresaNombre { get; set; }
    }

    public class CxcEmpresaClienteRowDto
    {
        public int EmpresaId { get; set; }
        public int ClienteId { get; set; }
        public string Codigo { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string Cedula { get; set; } = "";
        public int DiaCorte { get; set; }
        public decimal SaldoOriginal { get; set; }
        public decimal TotalCobrar { get; set; }
        public decimal TotalDisponible { get; set; }
        
    }
}
