namespace Consumo_App.Models
{
    public class ProveedorCaja
    {
        public int Id { get; set; }
        public int TiendaId { get; set; }             // ← mantener
        public ProveedorTienda Tienda { get; set; } = null!;

        public string Nombre { get; set; } = null!;
        public bool Activo { get; set; } = true;
    }
}
