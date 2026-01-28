namespace Consumo_App.DTOs
{
    public class EmpleadoDto
    {
        public record EmpleadoDtos(
        int Id,
        string Codigo,
        string Nombre,
        string? Cedula,
        string? Grupo,
        bool Activo,
        decimal Saldo);
    }
}
