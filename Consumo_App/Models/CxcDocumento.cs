using Consumo_App.Models;
using Consumo_App.Models.Pagos;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/*[Table("CxcDocumentos")]
public class CxcDocumento
{
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string NumeroDocumento { get; set; } = string.Empty;

    [Required]
    public int EmpresaId { get; set; }

    // Período del consolidado
    public DateTime PeriodoDesde { get; set; }
    public DateTime PeriodoHasta { get; set; }

    // Montos
    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoPagado { get; set; } = 0;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoPendiente { get; set; }

    // Estadísticas
    public int CantidadConsumos { get; set; } = 0;
    public int CantidadEmpleados { get; set; } = 0;

    // Estado
    public EstadoCxc Estado { get; set; } = EstadoCxc.Pendiente;

    // Fechas
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public DateTime FechaVencimiento { get; set; }

    // Auditoría
    public bool Anulado { get; set; } = false;
    public DateTime? AnuladoUtc { get; set; }
    public int? AnuladoPorUsuarioId { get; set; }

    [MaxLength(500)]
    public string? MotivoAnulacion { get; set; }

    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public int? CreadoPorUsuarioId { get; set; }

    // Navegación
    public Empresa Empresa { get; set; } = null!;
    public Usuario? CreadoPorUsuario { get; set; }
    public Usuario? AnuladoPorUsuario { get; set; }
    public ICollection<CxcDocumentoDetalle> Detalles { get; set; } = new List<CxcDocumentoDetalle>();
    public ICollection<CxcPago> Pagos { get; set; } = new List<CxcPago>();
    public ICollection<RefinanciamientoPago> Refinanciamientos { get; set; } = new List<RefinanciamientoPago>();

}*/