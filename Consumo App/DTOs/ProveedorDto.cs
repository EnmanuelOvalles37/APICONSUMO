namespace Consumo_App.DTOs
{
    public class ProveedorTiendaDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
    }

    public class ProveedorDetailDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Rnc { get; set; }
        public bool Activo { get; set; }
        public string? Direccion { get; set; }
        public string? Telefono { get; set; }
        public string? Email  { get; set; }
        public string? Contacto { get; set; }
        public int? DiasCorte { get; set; }
        public decimal PorcentajeComision { get; set; }
        public DateTime CreadoUtc { get; set; }

        public List<ProveedorTiendaDto> Tiendas { get; set; } = new();
    }
}
