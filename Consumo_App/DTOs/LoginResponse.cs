namespace Consumo_App.DTOs
{
    public class LoginResponse
    {
        public string? Token { get; set; }
        public string? Usuario { get; set; }
        public string? Rol { get; set; }
        public DateTime Expiracion { get; set; }
        public List<string>? Permisos { get; set; }
    }
}
