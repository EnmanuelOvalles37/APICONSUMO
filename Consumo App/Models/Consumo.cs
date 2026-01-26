using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{

    public class Consumo
    {
        
        public int Id { get; set; }
        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        public int ClienteId { get; set; }
        public Cliente Cliente { get; set; } = null!;
        public int EmpresaId { get; set; }     
        
        public int ProveedorId { get; set; }
        public Proveedor Proveedor { get; set; } = null!;

        public int TiendaId { get; set; }                
        public ProveedorTienda Tienda { get; set; } = null!;

        public int CajaId { get; set; }                  
        public ProveedorCaja Caja { get; set; } = null!;

        public decimal Monto { get; set; }
        public string? Nota { get; set; }
        public string? Referencia { get; set; }    // factura 
        public string? Concepto { get; set; }      // descripción libre

        public int UsuarioRegistradorId { get; set; }
        public Usuario UsuarioRegistrador { get; set; } = null!;

        public bool Reversado { get; set; } = false;  
        public DateTime? ReversadoUtc { get; set; }   
        public int? ReversadoPorUsuarioId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal PorcentajeComision { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoComision { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoNetoProveedor { get; set; } = 0;
        //public int? CxcDocumentoDetalleId { get; set; }
        //public int? CxpDocumentoDetalleId { get; set; }
    }

}
