public class PermisoDto
{
    public PermisoDto(int id, string codigo, string nombre, string ruta)
    {
        Id = id;
        Codigo = codigo;
        Nombre = nombre;
        Ruta = ruta;
    }

    public int Id { get; set; }
    public string Codigo { get; set; }
    public string Nombre { get; set; }
    public string Ruta { get; set; }
    public bool Seleccionado { get; set; }
}