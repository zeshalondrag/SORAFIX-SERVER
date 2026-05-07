namespace sorafix_api.Models.DTO;

public class EmailVerification
{
    public string Code { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Type { get; set; }
}
