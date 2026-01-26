namespace Consumo_App.DTOs
{
    public class EmpresasDtos
    {
        //public record PagedResult<T>(IReadOnlyList<T> Data, int Total);


        public class EmpresaListDto
        {
            public int Id { get; set; }
            public string Rnc { get; set; } = "";
            public string Nombre { get; set; } = "";
            public int Empleados { get; set; } = 0;          // <- conteo
            public DateTime? CreatedAt { get; set; } = DateTime.Now;
            public bool Activo { get; set; }
        }

        // DETALLE
        public class EmpresaDetailDto
        {
            public int Id { get; set; }
            public string Rnc { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string? Telefono { get; set; }
            public string? Email { get; set; }
            public string? Direccion { get; set; }
            public bool Activo { get; set; }
            public DateTime? CreadoEn { get; set; }
            public decimal LimiteCredito { get; set; }
            public int? DiaCorte { get; set; }

            public List<EmpresaEmpleadoDto> Empleados { get; set; } = new();
        }

        public class EmpresaEmpleadoDto
        {
            //empleado mostrado en detalle de empresa
            public int Id { get; set; }
            public string Codigo { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string Cedula { get; set; } = "";
            public string Grupo { get; set; } = "";
            public decimal Saldo { get; set; }
            public bool Activo { get; set; }
        }
        public class CreateEmpresaDto
        {
            public int Id { get; set; }
            public string Rnc { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string? Telefono { get; set; }
            public string? Email { get; set; }
            public string? Direccion { get; set; }
            public bool Activo { get; set; }
            public DateTime? CreadoEn { get; set; }
            public decimal LimiteCredito { get; set; }

        }
        public record EmpresaUpdateDto(
        string? Nombre,
        string? Rnc,
        decimal? Limite_Credito,
        bool? Activo,
        string? Telefono,
        string? Email,
        string? Direccion);
    }

    public class ActualizarDiaCorteDto
    {
        public int DiaCorte { get; set; }
    }
}


       
