namespace sorafix_api.Models.DTO;

public class CreateRequest
{
    public int RequestTypeId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
}