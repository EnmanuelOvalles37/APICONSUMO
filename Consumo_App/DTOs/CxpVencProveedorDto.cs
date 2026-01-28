namespace Consumo_App.DTOs
{
    public class CxpVencProveedorDto
    {
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
        public decimal NoVencido { get; set; }
        public decimal D0_30 { get; set; }
        public decimal D31_60 { get; set; }
        public decimal D61_90 { get; set; }
        public decimal D90p { get; set; }
        public decimal Total => NoVencido + D0_30 + D31_60 + D61_90 + D90p;
        public int Facturas { get; set; }
    }
}
