using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class User
{
    public int Id { get; set; }

    public int RoleId { get; set; }

    public string LastName { get; set; } = null!;

    public string FirstName { get; set; } = null!;

    public string? MiddleName { get; set; }

    public string Email { get; set; } = null!;

    public bool EmailVerified { get; set; }

    public string Phone { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public long? TgChatId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
    [JsonIgnore]
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    [JsonIgnore]
    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
    [JsonIgnore]
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    [JsonIgnore]
    public virtual ICollection<Request> RequestClients { get; set; } = new List<Request>();
    [JsonIgnore]
    public virtual ICollection<Request> RequestEmployees { get; set; } = new List<Request>();
    [JsonIgnore]
    public virtual ICollection<RequestStatusHistory> RequestStatusHistories { get; set; } = new List<RequestStatusHistory>();
    [JsonIgnore]
    public virtual Role Role { get; set; } = null!;
    [JsonIgnore]
    public virtual ICollection<VerificationCode> VerificationCodes { get; set; } = new List<VerificationCode>();
}