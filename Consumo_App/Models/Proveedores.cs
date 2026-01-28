// Proveedor.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Consumo_App.Data;
//using static Consumo_App.Data.AppDbContext;

namespace Consumo_App.Models;


[Table("Proveedores")]
public class Proveedor
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Rnc { get; set; }

    [MaxLength(200)]
    public string? Direccion { get; set; }

    [MaxLength(50)]
    public string? Telefono { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(200)]
    public string? Contacto { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;

    // *** CAMPOS DE CONFIGURACIÓN DE PAGOS ***

    /// <summary>
    /// Día del mes para realizar el corte (1-31).
    /// </summary>
    public int? DiasCorte { get; set; }

    /// <summary>
    /// Porcentaje de comisión/margen de ganancia.
    /// Ejemplo: 15.00 = 15%
    /// </summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal PorcentajeComision { get; set; } = 0;

    // Navegación
    public ICollection<ProveedorTienda> Tiendas { get; set; } = new List<ProveedorTienda>();
    //public ICollection<Consumo> Consumos { get; set; } = new List<Consumo>();
   
    public ICollection<ProveedorAsignacion> Asignaciones { get; set; } = new List<ProveedorAsignacion>();
    public ICollection<UsuarioProveedor> UsuariosProveedores { get; set; } = new List<UsuarioProveedor>();
}


public class ProveedorDto
{
    public string Nombre { get; set; } = string.Empty;
    public string? Rnc { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Contacto { get; set; }
    public bool Activo { get; set; } = true;

    // Configuración de pagos
    public int? DiasCorte { get; set; }
    public decimal PorcentajeComision { get; set; } = 0;
}

// DTO para mostrar consumo con comisión (solo admin)
public class ConsumoConComisionDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;

    // Montos
    public decimal MontoBruto { get; set; }          // Monto original
    public decimal PorcentajeComision { get; set; } // % aplicado
    public decimal MontoComision { get; set; }       // Tu ganancia
    public decimal MontoNetoProveedor { get; set; } // Lo que pagas al proveedor

    public bool Reversado { get; set; }
}

// DTO para documento CxP con desglose
public class DocumentoCxpConComisionDto
{
    public int Id { get; set; }
    public string NumeroDocumento { get; set; } = string.Empty;
    public string ProveedorNombre { get; set; } = string.Empty;
    public DateTime FechaEmision { get; set; }
    public DateTime FechaVencimiento { get; set; }

    // Desglose de montos
    public decimal MontoBruto { get; set; }    // Total de consumos
    public decimal MontoComision { get; set; } // Tu ganancia
    public decimal MontoTotal { get; set; }    // Neto a pagar
    public decimal MontoPagado { get; set; }
    public decimal MontoPendiente { get; set; }

    public string Estado { get; set; } = string.Empty;
    public int CantidadConsumos { get; set; }
}

// DTO para detalle de consumo en documento CxP
public class DetalleCxpConComisionDto
{
    public int ConsumoId { get; set; }
    public DateTime Fecha { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string? Concepto { get; set; }

    public decimal MontoBruto { get; set; }
    public decimal MontoComision { get; set; }
    public decimal MontoNeto { get; set; }
}
