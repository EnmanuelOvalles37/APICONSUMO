
// Models/Pagos/Models_CxC.cs
// Modelos para Cuentas por Cobrar (CxC) - Cobros a Empresas
// VERSIÓN CORREGIDA - Sin navegación problemática a Consumo

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Consumo_App.Models.Pagos
{
    // =====================================================================
    // ENUMS
    // =====================================================================

    public enum EstadoCxc
    {
        Pendiente = 0,
        ParcialmentePagado = 1,
        Pagado = 2,
        Vencido = 3,
        Refinanciado = 4,
        Anulado = 5
    }

    
    public enum MetodoPago
    {
        Efectivo = 0,
        Transferencia = 1,
        Cheque = 2,
        TarjetaCredito = 3,
        TarjetaDebito = 4,
        Otro = 5
    }

    public enum EstadoRefinanciamiento
    {
        Pendiente = 0,
        ParcialmentePagado = 1,
        Pagado = 2,
        Vencido = 3,
        Castigado = 4,  // Deuda irrecuperable
        Anulado = 5
    }

    // =====================================================================
    // CONFIGURACIÓN DE CORTE
    // =====================================================================
    [Table("ConfiguracionCortes", Schema = "dbo")]
    public class ConfiguracionCorte
    {
        public int Id { get; set; }

        [Required]
        public int EmpresaId { get; set; }

        public int DiaCorte { get; set; } = 1;
        public int DiasGracia { get; set; } = 5;
        public bool CorteAutomatico { get; set; } = true;
        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;

        // Navegación
        public Empresa Empresa { get; set; } = null!;
    }

    // =====================================================================
    // DOCUMENTO CXC (Factura a Empresa)
    // =====================================================================

    
    public class CxcDocumento
    {
        public int Id { get; set; }

        [Required]
        public int EmpresaId { get; set; }

        [MaxLength(20)]
        public string NumeroDocumento { get; set; } = string.Empty;

        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
        public DateTime FechaVencimiento { get; set; }
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }

        public int CantidadConsumos { get; set; } = 0;
        public int CantidadEmpleados { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoPagado { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoPendiente { get; set; }

        public EstadoCxc Estado { get; set; } = EstadoCxc.Pendiente;

        public bool Anulado { get; set; } = false;
        public DateTime? AnuladoUtc { get; set; }
        public int? AnuladoPorUsuarioId { get; set; }
        [MaxLength(500)]
        public string? MotivoAnulacion { get; set; }

        public bool Refinanciado { get; set; } = false;
        public DateTime? FechaRefinanciamiento { get; set; }

        [MaxLength(500)]
        public string? Notas { get; set; }

        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
        public int? CreadoPorUsuarioId { get; set; }

        // Navegaciones
        public Empresa Empresa { get; set; } = null!;
        public Usuario? CreadoPorUsuario { get; set; } = null!;
        public Usuario? AnuladoPorUsuario { get; set; }
        public ICollection<CxcDocumentoDetalle> Detalles { get; set; } = new List<CxcDocumentoDetalle>();
        public ICollection<CxcPago> Pagos { get; set; } = new List<CxcPago>();
        public RefinanciamientoDeuda? Refinanciamiento { get; set; }
    }

    // =====================================================================
    // DETALLE DE DOCUMENTO CXC - SIN NAVEGACIÓN A CONSUMO
    // =====================================================================
    [Table("CxcDocumentoDetalles")]
    public class CxcDocumentoDetalle
    {
        public int Id { get; set; }

        [Required]
        public int CxcDocumentoId { get; set; }

        [Required]
        public int ConsumoId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        // Navegación solo al documento padre
        public CxcDocumento CxcDocumento { get; set; } = null!;

        // *** NO HAY NAVEGACIÓN A CONSUMO ***
    }

    // =====================================================================
    // PAGO CXC (Cobro recibido)
    // =====================================================================
    [Table("CxcPagos")]
    public class CxcPago
    {
        public int Id { get; set; }

        [Required]
        public int CxcDocumentoId { get; set; }

        [MaxLength(20)]
        public string NumeroRecibo { get; set; } = string.Empty;

        // Alias para compatibilidad con código que usa NumeroComprobante
        [NotMapped]
        public string NumeroComprobante
        {
            get => NumeroRecibo;
            set => NumeroRecibo = value;
        }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        public MetodoPago MetodoPago { get; set; } = MetodoPago.Transferencia;

        [MaxLength(100)]
        public string? Referencia { get; set; }

        [MaxLength(100)]
        public string? Banco { get; set; }

        // Alias para compatibilidad
        [NotMapped]
        public string? BancoOrigen
        {
            get => Banco;
            set => Banco = value;
        }

        [MaxLength(500)]
        public string? Notas { get; set; }

        public bool Anulado { get; set; } = false;
        public DateTime? AnuladoUtc { get; set; }
        public int? AnuladoPorUsuarioId { get; set; }
        [MaxLength(500)]
        public string? MotivoAnulacion { get; set; }

        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
        public int RegistradoPorUsuarioId { get; set; }

        // Navegaciones
        public CxcDocumento CxcDocumento { get; set; } = null!;
        public Usuario RegistradoPorUsuario { get; set; } = null!;
        public Usuario? AnuladoPorUsuario { get; set; }
    }

    // =====================================================================
    // REFINANCIAMIENTO
    // =====================================================================

    public class RefinanciamientoDeuda
    {
        public int Id { get; set; }

        [Required]
        public int CxcDocumentoId { get; set; }

        [Required]
        public int EmpresaId { get; set; }

        [MaxLength(20)]
        public string NumeroRefinanciamiento { get; set; } = string.Empty;

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoOriginal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoPagado { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoPendiente { get; set; }

        public DateTime FechaVencimiento { get; set; }
        public EstadoRefinanciamiento Estado { get; set; } = EstadoRefinanciamiento.Pendiente;

        [MaxLength(500)]
        public string? Motivo { get; set; }

        [MaxLength(500)]
        public string? Notas { get; set; }

        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
        public int CreadoPorUsuarioId { get; set; }

        // Navegación
        public CxcDocumento CxcDocumento { get; set; } = null!;
        public Empresa Empresa { get; set; } = null!;
        public Usuario CreadoPorUsuario { get; set; } = null!;
        public ICollection<RefinanciamientoPago> Pagos { get; set; } = new List<RefinanciamientoPago>();
    }

    public class RefinanciamientoPago
    {
        public int Id { get; set; }

        [Required]
        public int RefinanciamientoId { get; set; }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        public MetodoPago MetodoPago { get; set; } = MetodoPago.Transferencia;

        [MaxLength(100)]
        public string? Referencia { get; set; }

        [MaxLength(500)]
        public string? Notas { get; set; }

        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
        public int RegistradoPorUsuarioId { get; set; }
        public bool Anulado { get; set; } = false;

        // Navegación
        public RefinanciamientoDeuda Refinanciamiento { get; set; } = null!;
        public Usuario RegistradoPorUsuario { get; set; } = null!;
    }

    public class DetalleConsumoDto
    {
        public int Id { get; set; }
        public int ConsumoId { get; set; }
        public DateTime Fecha { get; set; }
        public string EmpleadoNombre { get; set; } = "";
        public string EmpleadoCodigo { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public string TiendaNombre { get; set; } = "";
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
    }


} 

