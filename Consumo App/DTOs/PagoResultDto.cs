namespace Consumo_App.DTOs
{
    public class PagoResultDto
    {
        public int MovimientoId { get; set; }
        public int CxpId { get; set; }
        public decimal Monto { get; set; }
        public decimal BalanceAntes { get; set; }
        public decimal BalanceDespues { get; set; }
        public DateTime Fecha { get; set; }
    }
}
