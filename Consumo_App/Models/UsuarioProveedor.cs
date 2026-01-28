using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    [Table("UsuariosProveedores")]
    public class UsuarioProveedor
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public int UsuarioId { get; set; }

        [Required]
        [MaxLength(20)]
        public string RncProveedor { get; set; }

        // Navigation property
        [ForeignKey("RncProveedor")]
        public virtual Proveedor Proveedor { get; set; }
        public Usuario Usuario { get; set; } = null!;
    }
}
