using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;
using sorafix_api.Models.DTO;
using System.Security.Claims;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly SorafixContext _context;

        public UsersController(SorafixContext context)
        {
            _context = context;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            return await _context.Users
                .AsNoTracking()
                .ToListAsync();
        }

        [HttpGet("role/{roleId}")]
        public async Task<ActionResult<IEnumerable<User>>> GetUsersByRole(int roleId)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.RoleId == roleId && u.IsActive)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUser(int id)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();
            return user;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutUser(int id, User user)
        {
            if (id != user.Id) return BadRequest();

            user.UpdatedAt = DateTime.UtcNow;
            _context.Entry(user).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        [HttpPatch("{id}/role")]
        public async Task<IActionResult> ChangeRole(int id, [FromBody] ChangeRole dto)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == id) return BadRequest("Нельзя менять роль самому себе!");

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.RoleId = dto.RoleId;
            user.UpdatedAt = DateTime.UtcNow;

            var role = await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RoleId);
            var roleName = role?.Name ?? "ID: " + dto.RoleId;

            var admins = await _context.Users
                .AsNoTracking()
                .Where(u => u.RoleId == 1 && u.IsActive)
                .ToListAsync();

            foreach (var admin in admins)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = admin.Id,
                    Title = "Изменение прав доступа",
                    Message = $"Пользователю {user.LastName} {user.FirstName} назначена роль: {roleName}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("{id}/active")]
        public async Task<IActionResult> ToggleActive(int id, [FromBody] ToggleActive dto)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == id) return BadRequest("Нельзя деактивировать самого себя!");

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.IsActive = dto.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/Users/5/profile
        [HttpPatch("{id}/profile")]
        public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateUserProfile dto)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Unauthorized();

            if (currentUserId.Value != id) return Forbid();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (user.Email != dto.Email)
            {
                user.EmailVerified = false;
            }

            user.LastName = dto.LastName;
            user.FirstName = dto.FirstName;
            user.MiddleName = dto.MiddleName;
            user.Email = dto.Email;
            user.Phone = dto.Phone;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.Id == id);
        }
    }
}