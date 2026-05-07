using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class RequestStatusHistory
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public int StatusId { get; set; }

    public int ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; }
    [JsonIgnore]
    public virtual User ChangedByNavigation { get; set; } = null!;
    [JsonIgnore]
    public virtual Request Request { get; set; } = null!;
    [JsonIgnore]
    public virtual RequestStatus Status { get; set; } = null!;
}