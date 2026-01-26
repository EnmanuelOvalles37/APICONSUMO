using static Consumo_App.DTOs.EmpresasDtos;

namespace Consumo_App.DTOs
{
    public class EmpresaDetailDto
    {
        //public required EmpresaDto Empresa { get; init; }
        public required List<EmpleadoDto> Empleados { get; init; }
    }
}
