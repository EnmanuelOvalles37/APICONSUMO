using System.ComponentModel.DataAnnotations.Schema;

namespace Consumo_App.Models
{
    [Table("CxpMovimientos")]
    public class CxpMovimiento
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public DateTime Fecha { get; set; } = DateTime.UtcNow;
        public string Tipo { get; set; } = "FACT"; // FACT | PAGO | AJUSTE | REVERSO
        public string? Descripcion { get; set; }

        public decimal Debe { get; set; }   // +deuda
        public decimal Haber { get; set; }  // -deuda

        public int? ConsumoId { get; set; } // enlaza con consumo si aplica
        public int? PagoId { get; set; }    // enlaza con pago si aplica
        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;

        public Proveedor Proveedor { get; set; } = default!;
    }

}
