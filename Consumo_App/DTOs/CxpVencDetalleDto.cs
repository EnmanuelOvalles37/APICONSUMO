namespace Consumo_App.DTOs
{
    public class CxpVencDetalleDto
    {
        public int Id { get; set; }

        public int ProveedorId;

        public int GetProveedorId()
        {
            return ProveedorId;
        }

        public void SetProveedorId(int value)
        {
            ProveedorId = value;
        }

        public string ProveedorNombre { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Documento { get; set; } = "";
        public string? Concepto { get; set; }
        public decimal Monto { get; set; }
        public decimal Balance { get; set; }
        public int Dias { get; set; } // días desde la Fecha hasta la fecha de corte
    }
}
