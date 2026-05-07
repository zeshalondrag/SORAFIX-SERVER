using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class RequestStatus
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;
    [JsonIgnore]
    public virtual ICollection<RequestStatusHistory> RequestStatusHistories { get; set; } = new List<RequestStatusHistory>();
    [JsonIgnore]
    public virtual ICollection<Request> Requests { get; set; } = new List<Request>();
}