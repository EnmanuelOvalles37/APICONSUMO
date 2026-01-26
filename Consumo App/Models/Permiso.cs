using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    [Table("Permisos")]
    public class Permiso
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Ruta { get; set; } = string.Empty;

        // Navigation properties
        public virtual ICollection<RolPermiso> RolPermisos { get; set; }

        public Permiso()
        {
            RolPermisos = new HashSet<RolPermiso>();
        }
    }
}
