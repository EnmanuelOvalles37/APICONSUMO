using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    [Table("RolesPermisos")]
    public class RolPermiso
    {
        [Required]
        public int RolId { get; set; }

        [Required]
        public int PermisoId { get; set; }

        // Navigation properties
        [ForeignKey("RolId")]
        public virtual Rol Rol { get; set; }

        [ForeignKey("PermisoId")]
        public virtual Permiso Permiso { get; set; }
    }
}
