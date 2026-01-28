namespace Consumo_App.Models
{
    public class ProveedorAsignacion
    {
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public Proveedor Proveedor { get; set; } = null!;

        public int UsuarioId { get; set; }                      // tu AppUser.Id
        public Usuario Usuario { get; set; } = null!;

        public int? TiendaId { get; set; }
        public ProveedorTienda? Tienda { get; set; }

        public int? CajaId { get; set; }
        public ProveedorCaja? Caja { get; set; }

        public string Rol { get; set; } = "cajero";             // "cajero" | "supervisor" | "admin"
        public bool Activo { get; set; } = true;
    }
}
