using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class TelegramController : ControllerBase
    {
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

        public TelegramController(IMemoryCache cache, IConfiguration config)
        {
            _cache = cache;
            _config = config;
        }

        [HttpGet("connect-link")]
        public IActionResult GetConnectLink()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

            var code = Guid.NewGuid().ToString("N")[..10];

            _cache.Set($"tg_code_{code}", userId, TimeSpan.FromMinutes(15));

            var botUsername = _config["Telegram:BotUsername"];
            var link = $"https://t.me/{botUsername}?start={code}";

            return Ok(new { link });
        }
    }
}