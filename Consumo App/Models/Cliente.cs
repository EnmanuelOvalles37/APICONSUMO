using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Consumo_App.Models.Pagos;

namespace Consumo_App.Models
{
    [Table("Clientes")]
    public class Cliente
    {
        [Key]
        
        public int Id { get; set; }
        [Required]
        [MaxLength(50)]
        public string Codigo { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; }

        public string Cedula { get; set; }

       
        [MaxLength(50)]
        public string Grupo { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Saldo { get; set; }

        //[Required]
        //public int DiaCorte { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal SaldoOriginal { get; set; }

        [Required]
        public bool Activo { get; set; } = true;

        // Navigation properties
        public virtual ICollection<Consumo> Consumos { get; set; }
        public int EmpresaId { get; set; }
        public Empresa? Empresa { get; set; }


        public Cliente()
        {
            Consumos = new HashSet<Consumo>();
        }      

        //public ICollection<CxcDocumento> CxcDocumento { get; set; } = new List<CxcDocumento>();
    }
}
