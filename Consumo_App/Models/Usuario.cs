using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Consumo_App.Models
{
    public class Usuario
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string? Nombre { get; set; }

        [Required]
        public string? Contrasena { get; set; }

        public int RolId { get; set; }

        [ForeignKey("RolId")]
        public virtual Rol? Rol { get; set; }

        public bool Activo { get; set; } = true;
        [NotMapped]
        public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
        public int AccessFailedCount { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }
        public ICollection<UsuarioProveedor> UsuariosProveedores { get; set; } = new List<UsuarioProveedor>();
    }
    //public object? UsuariosProveedores { get; internal set; }

    // public int UsuarioId { get; internal set; }


}

