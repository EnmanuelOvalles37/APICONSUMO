using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    [Table("Roles")]
    public class Rol
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nombre { get; set; }

        [MaxLength(200)]
        public string Descripcion { get; set; }

        // Navigation properties
        public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
        public virtual ICollection<RolPermiso> RolPermisos { get; set; }

        public Rol()
        {
            Usuarios = new HashSet<Usuario>();
            RolPermisos = new HashSet<RolPermiso>();
        }
    }
}
