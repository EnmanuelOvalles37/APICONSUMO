// UsuarioDtos.cs
namespace Consumo_App.DTOs
{
    public class UsuarioDtos
    {
     
        public record UsuarioChangePasswordDto(string ContrasenaActual, string ContrasenaNueva);

        public class RolListDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = "";
            public string? Descripcion { get; set; }
        }
        public class UsuarioListDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = "";
            public string RolNombre { get; set; } = "";
            public bool Activo { get; set; }
            public DateTime CreadoUtc { get; set; }
        }
        public record RolCreateDto(string Nombre, string Descripcion);
        public record RolUpdateDto(string? Nombre, string? Descripcion);
        public record RolPermisosDto(int RolId, IReadOnlyList<int> PermisoIds);
    }

    public class UsuarioQueryDto
    {
        public string? q { get; set; }         // búsqueda por nombre
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public bool? Activo { get; set; }
        public int? RolId { get; set; }
    }

     public class UsuarioCreateDto
     {
         public string Nombre { get; set; } = default!;
         public string Contrasena { get; set; } = default!;
         public int RolId { get; set; }
         public bool Activo { get; set; } = true;
     } 

   

     public class UsuarioUpdateDto
    {
        public string Nombre { get; set; } = default!;
        public int? RolId { get; set; }
        public bool? Activo { get; set; }
    } 
   

     public class UsuarioResetPassDto
     {
         public string NuevaContrasena { get; set; } = default!;
     } 

  
}
