namespace Consumo_App.Models
{
    public class ProveedorTienda
    {
        
        public int Id { get; set; }
        public int ProveedorId { get; set; }
        public Proveedor Proveedor { get; set; } = null!;
        public string Nombre { get; set; } = null!; // "Sucursal Kennedy"
        public bool Activo { get; set; } = true;

        public ICollection<ProveedorCaja> Cajas { get; set; } = new List<ProveedorCaja>();
    }
}

