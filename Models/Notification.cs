using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? RequestId { get; set; }

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
    [JsonIgnore]
    public virtual Request? Request { get; set; }
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}