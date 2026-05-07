using System.Net;

namespace sorafix_api.Models;

public partial class AuditLog
{
    public long Id { get; set; }

    public string TableName { get; set; } = null!;

    public string Operation { get; set; } = null!;

    public int RecordId { get; set; }

    public string? OldData { get; set; }

    public string? NewData { get; set; }

    public IPAddress? UserIp { get; set; }

    public int? UserId { get; set; }

    public DateTime CreatedAt { get; set; }
}