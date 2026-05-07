using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class VerificationCode
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Type { get; set; } = null!;

    public string Code { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}