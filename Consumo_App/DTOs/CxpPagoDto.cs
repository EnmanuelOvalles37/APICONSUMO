namespace Consumo_App.DTOs
{
    public class CxpPagoDto
    {
        public int Id { get; set; }
        public int CxpId { get; set; }
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; } = "ABONO";
        public decimal Monto { get; set; }
        public string MedioPago { get; set; } = "";
        public string? Referencia { get; set; }
        public string? Observacion { get; set; }
        public int? UsuarioId { get; set; }
    }
}
