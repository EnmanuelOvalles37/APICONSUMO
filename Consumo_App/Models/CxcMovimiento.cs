/*using System.ComponentModel.DataAnnotations.Schema;

[Table("CxcMovimiento")]
public class CxcMovimiento
{
    public int Id { get; set; }
    public int CxcId { get; set; }
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "ABONO";   // 'CARGO'|'ABONO'|'AJUSTE'|'REVERSO'
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
    public string? MedioPago { get; set; }
    public string? Observacion { get; set; }
    public int? UsuarioId { get; set; }

    public CxcDocumento Cxc { get; set; } = default!;
} */

// Models/CxcMovimiento.cs
// NOTA: Este modelo es para compatibilidad con el sistema antiguo.
// El nuevo sistema usa CxcPago para registrar cobros.

using System.ComponentModel.DataAnnotations.Schema;
using Consumo_App.Models.Pagos;

namespace Consumo_App.Models
{
    [Table("CxcMovimiento")]
    public class CxcMovimiento
    {
        public int Id { get; set; }

        /// <summary>
        /// FK al documento CxC (nuevo sistema)
        /// </summary>
        public int? CxcDocumentoId { get; set; }

        /// <summary>
        /// FK antigua (mantener por compatibilidad con datos existentes)
        /// </summary>
        public int? CxcId { get; set; }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tipo de movimiento: 'CARGO'|'ABONO'|'AJUSTE'|'REVERSO'
        /// </summary>
        public string Tipo { get; set; } = "ABONO";

        public decimal Monto { get; set; }

        public string? Referencia { get; set; }
        public string? MedioPago { get; set; }
        public string? Observacion { get; set; }
        public int? UsuarioId { get; set; }

        // Navegación al nuevo CxcDocumento
        [ForeignKey("CxcDocumentoId")]
        public CxcDocumento? CxcDocumento { get; set; }
    }
}