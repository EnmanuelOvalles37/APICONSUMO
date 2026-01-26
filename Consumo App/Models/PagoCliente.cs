using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    [Table("PagosClientes")]
    public class PagoCliente
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(20)]
        public string Cedula { get; set; }

        [Required]
        [MaxLength(50)]
        public string Grupo { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        [Required]
        public DateTime Fecha { get; set; }

        [Required]
        [MaxLength(50)]
        public string Usuario { get; set; }

        [MaxLength(200)]
        public string Nota { get; set; }

        [Required]
        [MaxLength(20)]
        public string Secuencia { get; set; }
    }
}
