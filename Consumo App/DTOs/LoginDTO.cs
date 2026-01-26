using System.ComponentModel.DataAnnotations;

namespace Consumo_App.DTOs
{
    public class LoginDTO
    {
        [Required]
        public string? Usuario { get; set; }

        [Required]
        public string? Contrasena { get; set; }
    }
}
