using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace sorafix_api.Services
{
    public interface INotificationService
    {
        Task SendTelegramNotificationAsync(int userId, string message, ReplyMarkup? replyMarkup = null);
    }

    public class NotificationService : INotificationService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceScopeFactory _scopeFactory; 

        public NotificationService(ITelegramBotClient botClient, IServiceScopeFactory scopeFactory)
        {
            _botClient = botClient;
            _scopeFactory = scopeFactory;
        }

        public async Task SendTelegramNotificationAsync(int userId, string message, ReplyMarkup? replyMarkup = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SorafixContext>();

            var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.TgChatId != null)
            {
                try
                {
                    await _botClient.SendMessage(
                        chatId: user.TgChatId.Value,
                        text: message,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: replyMarkup
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TG ERROR]: Не удалось отправить сообщение пользователю {userId}. Ошибка: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[TG INFO]: У пользователя {userId} не привязан Telegram.");
            }
        }
    }
}