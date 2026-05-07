namespace sorafix_api.Models.DTO;

public class ChatAttachmentResponse
{
    public string FilePath { get; set; } = null!;
    public string OriginalName { get; set; } = null!;
    public string FileType { get; set; } = null!;
}