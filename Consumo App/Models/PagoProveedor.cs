// PagoProveedor.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Consumo_App.Models;

public class PagoProveedor
{
    public int Id { get; set; }
    public int ProveedorId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public decimal Monto { get; set; }
    public string? Metodo { get; set; }    // transferencia, efectivo, etc.
    public string? Referencia { get; set; }

    public Proveedor Proveedor { get; set; } = default!;
}
