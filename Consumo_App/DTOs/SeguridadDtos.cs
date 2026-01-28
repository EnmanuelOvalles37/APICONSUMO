namespace Consumo_App.DTOs
{
    public class SeguridadDtos
    {
        public record SeguridadListDto(int Id, string Nombre, string Rol, bool Activo);
        public record UsuarioCreateDto(string Nombre, string Contrasena, int RolId, bool Activo = true);
        public record UsuarioUpdateDto(string Nombre, int? RolId, bool? Activo);
        public record UsuarioPasswordDto(string NuevaContrasena);

        public class RolDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = "";
            public string? Descripcion { get; set; }

            public RolDto() { }
            public RolDto(int id, string nombre, string? descripcion)
            {
                Id = id;
                Nombre = nombre;
                Descripcion = descripcion;
            }
        }
        public class PermisoDto
        {
            public int Id { get; set; }
            public string Codigo { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string? Ruta { get; set; }

            public PermisoDto() { }
            public PermisoDto(int id, string codigo, string nombre, string? ruta)
            {
                Id = id;
                Codigo = codigo;
                Nombre = nombre;
                Ruta = ruta;
            }
        }
        public record RolPermisoCheckDto(int Id, string Codigo, string Nombre, bool Asignado);
        public record RolPermisosUpdateDto(int[] PermisoIds);
    }
}
