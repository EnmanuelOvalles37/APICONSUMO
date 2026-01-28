
using Consumo_App.Models;
using System.ComponentModel.DataAnnotations.Schema;

 
public class CuentasPorCobrar
{
    public int Id { get; set; }

    
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = default!;

    public DateTime FechaEmision { get; set; }
    public string Documento { get; set; } = "";
    public string? Concepto { get; set; }
    public decimal Monto { get; set; }
    public decimal Impuesto { get; set; }
    public decimal Descuento { get; set; }
    public decimal MontoNeto { get; set; }
    
}
