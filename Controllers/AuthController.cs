using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using sorafix_api.Models;
using sorafix_api.Models.DTO;
using sorafix_api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace sorafix_api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly SorafixContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher _passwordHasher;

    public AuthController(SorafixContext context, IConfiguration configuration, IEmailService emailService, IPasswordHasher passwordHasher)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] Authorization dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (user == null || !_passwordHasher.VerifyPassword(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Неверный email или пароль" });

        if (!user.IsActive)
        {
            return StatusCode(403, new { message = "Ваш аккаунт деактивирован.", needsRestoration = true });
        }

        var token = await GenerateJwtToken(user);
        return Ok(new { token, user });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] Registration dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return BadRequest(new { message = "Email уже занят" });

        if (await _context.Users.AnyAsync(u => u.Phone == dto.Phone))
            return BadRequest(new { message = "Телефон уже занят" });

        var user = new User
        {
            RoleId = 4,
            LastName = dto.LastName,
            FirstName = dto.FirstName,
            MiddleName = dto.MiddleName,
            Email = dto.Email,
            Phone = dto.Phone,
            PasswordHash = _passwordHasher.HashPassword(dto.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            EmailVerified = false
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var admins = await _context.Users.Where(u => u.RoleId == 1).ToListAsync();
        foreach (var admin in admins)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = admin.Id,
                Title = "Новый клиент",
                Message = $"Клиент: {user.LastName} {user.FirstName} зарегистрировался в системе!",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        var token = await GenerateJwtToken(user);
        return Ok(new { token, user });
    }

    [Authorize]
    [HttpPost("deactivate")]
    public async Task<IActionResult> DeactivateAccount()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();

        var user = await _context.Users.FindAsync(int.Parse(userIdClaim));
        if (user == null) return NotFound();

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Аккаунт успешно деактивирован" });
    }

    [HttpPost("request-restore")]
    public async Task<IActionResult> RequestRestore([FromQuery] string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return NotFound(new { message = "Пользователь не найден" });
        if (user.IsActive) return BadRequest(new { message = "Аккаунт и так активен" });

        string rawCode = new Random().Next(100000, 999999).ToString();

        _context.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Type = "restore",
            Code = _passwordHasher.HashPassword(rawCode),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _context.SaveChangesAsync();

        string body = GetEmailTemplate("Восстановление доступа", rawCode, "Код действителен 10 минут.");
        await _emailService.SendEmailAsync(email, "Восстановление аккаунта SORAFIX", body);

        return Ok(new { message = "Код восстановления отправлен" });
    }

    [HttpPost("verify-restore")]
    public async Task<IActionResult> VerifyRestore([FromBody] VerifyEmailRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null) return NotFound();

        var validCode = await GetValidCode(user.Id, "restore", request.Code);
        if (validCode == null) return BadRequest(new { message = "Неверный или просроченный код" });

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        _context.VerificationCodes.Remove(validCode);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Аккаунт восстановлен", token = await GenerateJwtToken(user) });
    }

    [Authorize]
    [HttpGet("validate")]
    public IActionResult ValidateToken()
    {
        return Ok(new
        {
            IsValid = true,
            User = new
            {
                Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Email = User.FindFirst(ClaimTypes.Email)?.Value,
                Role = User.FindFirst(ClaimTypes.Role)?.Value
            }
        });
    }

    [HttpPost("verify-code")]
    public async Task<IActionResult> VerifyCode([FromQuery] string email, [FromQuery] string code, [FromQuery] string type)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return NotFound();

        var validCode = await GetValidCode(user.Id, type, code);
        if (validCode == null) return BadRequest(new { message = "Код невалиден" });

        if (type == "verification") user.EmailVerified = true;

        _context.VerificationCodes.Remove(validCode);
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [Authorize]
    [HttpPost("request-email-verification")]
    public async Task<IActionResult> RequestEmailVerification([FromQuery] string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return NotFound();
        if (user.EmailVerified) return BadRequest(new { message = "Почта уже подтверждена" });

        string rawCode = new Random().Next(100000, 999999).ToString();
        _context.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Type = "verification",
            Code = _passwordHasher.HashPassword(rawCode),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _context.SaveChangesAsync();

        string body = GetEmailTemplate("Подтверждение почты", rawCode, "Код действителен 10 минут.");
        await _emailService.SendEmailAsync(email, "Подтверждение почты SORAFIX", body);

        return Ok(new { message = "Код подтверждения отправлен" });
    }

    [HttpPost("request-password-reset")]
    public async Task<IActionResult> RequestPasswordReset([FromQuery] string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return NotFound();

        string rawCode = new Random().Next(100000, 999999).ToString();
        _context.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Type = "password",
            Code = _passwordHasher.HashPassword(rawCode),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _context.SaveChangesAsync();

        string body = GetEmailTemplate("Сброс пароля", rawCode, "Если вы не запрашивали смену пароля, проигнорируйте это письмо.");
        await _emailService.SendEmailAsync(email, "Сброс пароля SORAFIX", body);

        return Ok(new { message = "Код сброса отправлен" });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPassword dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (user == null) return NotFound();

        var validCode = await GetValidCode(user.Id, "password", dto.Code);
        if (validCode == null) return BadRequest(new { message = "Код невалиден" });

        user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        _context.VerificationCodes.Remove(validCode);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Пароль успешно изменен" });
    }

    private async Task<VerificationCode?> GetValidCode(int userId, string type, string rawCode)
    {
        var codes = await _context.VerificationCodes
            .Where(v => v.UserId == userId && v.Type == type && v.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        return codes.FirstOrDefault(c => _passwordHasher.VerifyPassword(rawCode, c.Code));
    }

    private string GetEmailTemplate(string title, string code, string footer)
    {
        return $@"
        <div style='font-family: Arial, sans-serif; border: 1px solid #059467; padding: 20px; border-radius: 10px; text-align: center; max-width: 500px; margin: 0 auto;'>
            <h2 style='color: #059467; margin-top: 0;'>SORAFIX</h2>
            <p style='color: #333;'>{title}. Ваш код:</p>
            <h1 style='letter-spacing: 5px; color: #333; background-color: #f4f4f4; padding: 10px; border-radius: 5px; display: inline-block;'>{code}</h1>
            <p style='font-size: 12px; color: #666; margin-top: 20px;'>{footer}</p>
        </div>";
    }

    private async Task<string> GenerateJwtToken(User user)
    {
        var role = await _context.Roles.FindAsync(user.RoleId);
        var roleName = role?.Name ?? "Client";

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FirstName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, roleName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}