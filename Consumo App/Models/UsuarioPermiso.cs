

namespace Consumo_App.Models
{
    
    public class UsuarioPermiso
    {
        public int UsuarioId { get; set; }
        public int PermisoId { get; set; }
        public bool IsGranted { get; set; }
    }
}
