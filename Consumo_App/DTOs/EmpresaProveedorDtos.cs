namespace Consumo_App.DTOs
{
    public class EmpresaProveedorDtos
    {
        public record EmpresaListDto(int Id, string Rnc, string Nombre, bool Activo, int Empleados);
        public record EmpresaFormDto(string Rnc, string Nombre, string? Telefono, string? Email, string? Direccion, bool Activo);

        public record ProveedorListDto(int Id, string Rnc, string Nombre, bool Activo);
        public record ProveedorFormDto(string Nombre,
    string? Rnc,
    string? Direccion,
    string? Telefono,
    string? Email,
    string? Contacto,
    int? DiasCorte,
    decimal PorcentajeComision,
    bool Activo);
    }
}
