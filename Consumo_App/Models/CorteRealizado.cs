using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    [Table("CortesRealizados")]
    public class CorteRealizado
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public int IdCliente { get; set; }

        [Required]
        [MaxLength(50)]
        public string Grupo { get; set; }

        [Required]
        public int DiaCorte { get; set; }

        [Required]
        public DateTime FechaRestablecimiento { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoRestaurado { get; set; }

        [Required]
        [MaxLength(50)]
        public string Usuario { get; set; }

        
        [ForeignKey("IdCliente")]
        public virtual Cliente Cliente { get; set; }
    }
}
