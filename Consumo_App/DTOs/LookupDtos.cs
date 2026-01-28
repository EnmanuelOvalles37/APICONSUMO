namespace Consumo_App.DTOs
{
    public class LookupDtos
    {
        public record LookupItemDto(string value, string label);
        public record PagedLookupDto(IEnumerable<LookupItemDto> data, int page, int pageSize, int total);
    }
}
