using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class Request
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public int? EmployeeId { get; set; }

    public int RequestTypeId { get; set; }

    public int StatusId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public bool IsPriceConfirmed { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public bool IsPaid { get; set; }

    public string? YookassaPaymentId { get; set; }
    [JsonIgnore]
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    [JsonIgnore]
    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
    [JsonIgnore]
    public virtual User Client { get; set; } = null!;
    [JsonIgnore]
    public virtual User? Employee { get; set; }
    [JsonIgnore]
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    [JsonIgnore]
    public virtual ICollection<RequestStatusHistory> RequestStatusHistories { get; set; } = new List<RequestStatusHistory>();
    [JsonIgnore]
    public virtual RequestType RequestType { get; set; } = null!;
    [JsonIgnore]
    public virtual RequestStatus Status { get; set; } = null!;
}