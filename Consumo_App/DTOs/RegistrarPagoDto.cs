namespace Consumo_App.DTOs
{
    public class RegistrarPagoDto
    {
        public int CxpId { get; set; }
        public DateTime? Fecha { get; set; }      // opcional; si viene null => now (UTC)
        public decimal Monto { get; set; }
        public string MedioPago { get; set; } = "";   // EFECTIVO | TRANSFERENCIA | CHEQUE | TARJETA
        public string? Referencia { get; set; }
        public string? Observacion { get; set; }
    }
}
