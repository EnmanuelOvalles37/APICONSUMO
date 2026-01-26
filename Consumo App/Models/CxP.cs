
// Models/Pagos/Models_CxP.cs
// Modelos para Cuentas por Pagar (CxP) - Pagos a Proveedores
// VERSIÓN CORREGIDA - Sin navegación problemática a Consumo

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Consumo_App.Models.Pagos
{
    // =====================================================================
    // ENUMS
    // =====================================================================

    public enum EstadoCxp
    {
        Pendiente = 0,
        ParcialmentePagado = 1,
        Pagado = 2,
        Vencido = 3,
        Anulado = 4
    } 

    
    public enum TipoCxpDocumento
    {
        Factura = 0,
        ConsolidadoConsumos = 1,
        NotaCredito = 2,
        NotaDebito = 3
    }





    // =====================================================================
    // DOCUMENTO CXP (Factura de Proveedor)
    // =====================================================================

    /* public class CxpDocumento
     {
         public int Id { get; set; }

         [Required]
         public int ProveedorId { get; set; }

         [MaxLength(20)]
         public string NumeroDocumento { get; set; } = string.Empty;

         [MaxLength(50)]
         public string? NumeroFacturaProveedor { get; set; }

         public TipoCxpDocumento Tipo { get; set; } = TipoCxpDocumento.ConsolidadoConsumos;
         public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
         public DateTime FechaVencimiento { get; set; }
         public DateTime? PeriodoDesde { get; set; }
         public DateTime? PeriodoHasta { get; set; }

         public decimal MontoBruto { get; set; }    // Total de consumos
         public decimal MontoComision { get; set; } // Tu ganancia
         //public decimal MontoTotal { get; set; }    // Neto a pagar

         [Column(TypeName = "decimal(18,2)")]
         public decimal MontoTotal { get; set; }

         [Column(TypeName = "decimal(18,2)")]
         public decimal MontoPagado { get; set; } = 0;

         [Column(TypeName = "decimal(18,2)")]
         public decimal MontoPendiente { get; set; }

         public EstadoCxp Estado { get; set; } = EstadoCxp.Pendiente;

         [MaxLength(500)]
         public string? Concepto { get; set; }

         [MaxLength(500)]
         public string? Notas { get; set; }

         public bool Anulado { get; set; } = false;
         public DateTime? AnuladoUtc { get; set; }
         public int? AnuladoPorUsuarioId { get; set; }

         [MaxLength(500)]
         public string? MotivoAnulacion { get; set; }

         public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
         public int CreadoPorUsuarioId { get; set; }

         // Navegación
         public Proveedor Proveedor { get; set; } = null!;
         public Usuario CreadoPorUsuario { get; set; } = null!;
         public Usuario? AnuladoPorUsuario { get; set; }
         public ICollection<CxpDocumentoDetalle> Detalles { get; set; } = new List<CxpDocumentoDetalle>();
         public ICollection<CxpPago> Pagos { get; set; } = new List<CxpPago>();
     } //////// aqui va un asterisco para cerrar el comentario, empieza en la linea 41/
    */
    [Table("CxpDocumentos")]
    public class CxpDocumento
    {
        public int Id { get; set; }

        [Required]
        public int ProveedorId { get; set; }

        [MaxLength(20)]
        public string NumeroDocumento { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? NumeroFacturaProveedor { get; set; }

        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
        public DateTime FechaVencimiento { get; set; }
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }

        public int CantidadConsumos { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoBruto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoComision { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoPagado { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoPendiente { get; set; }

        public EstadoCxp Estado { get; set; } = EstadoCxp.Pendiente;

        [MaxLength(500)]
        public string? Concepto { get; set; }

        [MaxLength(500)]
        public string? Notas { get; set; }

        public bool Anulado { get; set; } = false;
        public DateTime? AnuladoUtc { get; set; }
        public int? AnuladoPorUsuarioId { get; set; }
        [MaxLength(500)]
        public string? MotivoAnulacion { get; set; }

        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
        public int CreadoPorUsuarioId { get; set; }

        // Navegaciones
        public Proveedor Proveedor { get; set; } = null!;
        public Usuario CreadoPorUsuario { get; set; } = null!;
        public Usuario? AnuladoPorUsuario { get; set; }
        public ICollection<CxpDocumentoDetalle> Detalles { get; set; } = new List<CxpDocumentoDetalle>();
        public ICollection<CxpPago> Pagos { get; set; } = new List<CxpPago>();
    }

    // =====================================================================
    // DETALLE DE DOCUMENTO CXP - SIN NAVEGACIÓN A CONSUMO
    // =====================================================================
    [Table("CxpDocumentoDetalles")]
    public class CxpDocumentoDetalle
    {
        public int Id { get; set; }

        [Required]
        public int CxpDocumentoId { get; set; }

        [Required]
        public int ConsumoId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoBruto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoComision { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoNeto { get; set; }

        // Navegación solo al documento padre
        public CxpDocumento CxpDocumento { get; set; } = null!;

        // *** NO HAY NAVEGACIÓN A CONSUMO ***
    } /////// aqui va un asterisco para cerrar el comentario, empieza en la linea 173/
    /*
public class CxpDocumentoDetalle
    {
        public int Id { get; set; }

        [Required]
        public int CxpDocumentoId { get; set; }

        [Required]
        public int ConsumoId { get; set; }

        /// <summary>
        /// Monto bruto del consumo (antes de comisión)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoBruto { get; set; } = 0;

        /// <summary>
        /// Monto de comisión del consumo
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoComision { get; set; } = 0;

        /// <summary>
        /// Monto neto (lo que se paga al proveedor por este consumo)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        // *** NAVEGACIÓN ***
        public CxpDocumento CxpDocumento { get; set; } = null!;
        public Consumo Consumo { get; set; } = null!;
    } */

    [Table("CxpPagos")]
    public class CxpPago
    {
        public int Id { get; set; }

        [Required]
        public int CxpDocumentoId { get; set; }

        [MaxLength(20)]
        public string NumeroComprobante { get; set; } = string.Empty;

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        public MetodoPago MetodoPago { get; set; } = MetodoPago.Transferencia;

        [MaxLength(100)]
        public string? Referencia { get; set; }

        [MaxLength(100)]
        public string? BancoOrigen { get; set; }

        [MaxLength(50)]
        public string? CuentaDestino { get; set; }

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
        public CxpDocumento CxpDocumento { get; set; } = null!;
        public Usuario RegistradoPorUsuario { get; set; } = null!;
        public Usuario? AnuladoPorUsuario { get; set; }
    }
}

//************************************



