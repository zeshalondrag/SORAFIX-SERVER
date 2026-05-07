using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class ChatMessage
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public int UserId { get; set; }

    public string? MessageText { get; set; }

    public bool IsEdited { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsSystem { get; set; }
    [JsonIgnore]
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    [JsonIgnore]
    public virtual Request Request { get; set; } = null!;
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}