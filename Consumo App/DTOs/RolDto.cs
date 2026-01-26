using System.Text.Json.Serialization;


public class RolDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "";
    [JsonPropertyName("descripcion")]
    public string Descripcion { get; set; } = "";
    [JsonPropertyName("permisos")]

    public List<PermisoDto> Permisos { get; set; } = new();

    public RolDto() { }

    public RolDto(int id, string nombre, string descripcion)
    {
        Id = id;
        Nombre = nombre;
        Descripcion = descripcion;
    }
}
