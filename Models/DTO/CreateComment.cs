namespace sorafix_api.Models.DTO;

public class CreateComment
{
    public int RequestId { get; set; }
    public string Text { get; set; } = null!;
}