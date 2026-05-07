namespace sorafix_api.Models.DTO;

public class ResetPassword
{
    public string Email { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
}