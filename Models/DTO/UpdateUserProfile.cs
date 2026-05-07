namespace sorafix_api.Models.DTO;

public class UpdateUserProfile
{
    public string LastName { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
}