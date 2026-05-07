namespace sorafix_api.Models.DTO;

public class VerifyEmailRequest
{
    public string Email { get; set; } = null!;
    public string Code { get; set; } = null!;
}