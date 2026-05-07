using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;
using sorafix_api.Services;
using System.Security.Claims;
using System.Text.Json;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentsController : ControllerBase
    {
        private readonly SorafixContext _context;
        private readonly YooKassaService _yooKassaService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(SorafixContext context, YooKassaService yooKassaService, INotificationService notificationService, ILogger<PaymentsController> logger)
        {
            _context = context;
            _yooKassaService = yooKassaService;
            _notificationService = notificationService;
            _logger = logger;
        }

        private int GetCurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        [HttpPost("{requestId}/generate")]
        [Authorize]
        public async Task<IActionResult> GeneratePayment(int requestId)
        {
            var request = await _context.Requests.Include(r => r.Client).FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null) return NotFound("Заявка не найдена");

            var currentUserId = GetCurrentUserId();
            if (request.ClientId != currentUserId) return Forbid();

            if (request.IsPaid) return BadRequest("Заявка уже оплачена");
            if (request.Price == null || request.Price <= 0) return BadRequest("Цена еще не установлена");

            var (paymentId, paymentUrl) = await _yooKassaService.CreatePaymentAsync(
                (decimal)request.Price,
                requestId,
                $"Оплата по заявке №{requestId}");

            request.YookassaPaymentId = paymentId;

            var sysMessage = new ChatMessage
            {
                RequestId = requestId,
                UserId = currentUserId,
                MessageText = $"Перейдите по ссылке для оплаты {request.Price} ₽:\n{paymentUrl}",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(sysMessage);
            await _context.SaveChangesAsync();

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("Оплатить заявку", paymentUrl)
                }
            });

            await _notificationService.SendTelegramNotificationAsync(
                request.ClientId,
                $"🔔 *Счет на оплату*\n\n" +
                $"📄 Заявка: *№{requestId}*\n" +
                $"💰 Сумма к оплате: *{request.Price} ₽*\n\n" +
                $"Для продолжения нажмите кнопку ниже 👇",
                keyboard
            );

            return Ok(new { paymentUrl });
        }

        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> YooKassaWebhook([FromBody] JsonElement payload)
        {
            try
            {
                var eventType = payload.GetProperty("event").GetString();

                if (eventType == "payment.succeeded")
                {
                    var paymentObj = payload.GetProperty("object");
                    var paymentId = paymentObj.GetProperty("id").GetString();
                    var metadata = paymentObj.GetProperty("metadata");

                    if (metadata.TryGetProperty("request_id", out var reqIdElem))
                    {
                        int requestId = int.Parse(reqIdElem.GetString()!);

                        var request = await _context.Requests.FirstOrDefaultAsync(r => r.Id == requestId);
                        if (request != null && !request.IsPaid)
                        {
                            request.IsPaid = true;

                            var sysMessage = new ChatMessage
                            {
                                RequestId = requestId,
                                UserId = request.ClientId,
                                MessageText = $"Оплата {request.Price} ₽ по заявке №{requestId} получена",
                                IsSystem = true,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            _context.ChatMessages.Add(sysMessage);
                            await _context.SaveChangesAsync();

                            await _notificationService.SendTelegramNotificationAsync(request.ClientId,
                                $"✅ *Оплата получена!*\n\n📄 Заявка №{requestId} оплачена.");

                            if (request.EmployeeId.HasValue)
                            {
                                await _notificationService.SendTelegramNotificationAsync(request.EmployeeId.Value,
                                    $"Клиент оплатил заявку №{requestId}.");
                            }
                        }
                    }
                }
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке вебхука ЮKassa");
                return BadRequest();
            }
        }
    }
}
