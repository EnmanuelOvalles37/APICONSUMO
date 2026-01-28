// Models/Empresa.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Consumo_App.Models;

[Table("Empresas")]
public class Empresa
{
    [Key] public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Rnc { get; set; } = default!;

    [Required, MaxLength(120)]
    public string Nombre { get; set; } = default!;

    [MaxLength(30)] public string? Telefono { get; set; }
    [MaxLength(120)] public string? Email { get; set; }
    [MaxLength(200)] public string? Direccion { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Cliente> Empleados { get; set; } = new List<Cliente>();
    [Column("Limite_Credito", TypeName = "decimal(18,2)")]
    public decimal LimiteCredito { get; set; } = 0m;
    public int? DiaCorte { get; set; }
}
