namespace sorafix_api.Models.DTO;

public class CommentResponse
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public string Text { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public int UserId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string RoleName { get; set; } = null!;
    public List<ChatAttachmentResponse> Attachments { get; set; } = new();
    public bool IsEdited { get; set; }
    public DateTime UpdatedAt { get; set; }
}