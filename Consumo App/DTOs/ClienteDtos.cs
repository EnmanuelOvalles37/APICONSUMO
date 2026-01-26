namespace Consumo_App.DTOs
{
    public class ClienteDtos
    {
        public int Id { get; set; }   // o int si corresponde
        public string? Nombre { get; set; }
        public string? Grupo { get; set; }
        public decimal Saldo { get; set; }
       // public int DiaCorte { get; set; }
        public decimal SaldoOriginal { get; set; }
        public bool Activo { get; set; }

        public string? EmpresaNombre { get; set; }
        public string? EmpresaRnc { get; set; }
    }

    public record ClienteListDto(
        int Id,
        string Codigo,
        string Nombre,
        string? Cedula,
        string Grupo,
        decimal Saldo,
        decimal SaldoOriginal,
        //int DiaCorte,
        bool Activo
);

    public record ClienteCreateDto(
        string? Codigo,
        string Nombre,
        string? Cedula,
        string? Grupo,
        decimal SaldoOriginal,
        //int DiaCorte,
        bool Activo = true
    );

    public record ClienteUpdateDto(
        string? Codigo,
        string? Nombre,
        string? Cedula,
        string? Grupo,
        decimal? SaldoOriginal,
        //int? DiaCorte,
        bool? Activo
    );

    // para el resultado del CSV
    public record BulkResultadoDto(
        int Insertados,
        int Actualizados,
        List<string> Errores
        );

   
    public record ClienteDetalleDto(
    int Id,
    string Codigo,
    string Nombre,
    string Cedula,
    int? EmpresaId,
    string EmpresaNombre,
    decimal Saldo,
    //int DiaCorte,
    decimal SaldoOriginal,
    bool Activo
);

    public record ClienteMatchDto(int ClienteId, string Nombre, int? EmpresaId, string EmpresaNombre, decimal Saldo);

}
