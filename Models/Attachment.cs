using System.Text.Json.Serialization;

namespace sorafix_api.Models;

public partial class Attachment
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public int? MessageId { get; set; }

    public int UploadedBy { get; set; }

    public string FilePath { get; set; } = null!;

    public string OriginalName { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public int FileSize { get; set; }

    public string AttachmentType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    [JsonIgnore]
    public virtual ChatMessage? Message { get; set; }
    [JsonIgnore]
    public virtual Request Request { get; set; } = null!;
    [JsonIgnore]
    public virtual User UploadedByNavigation { get; set; } = null!;
}