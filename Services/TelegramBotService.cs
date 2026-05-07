using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using sorafix_api.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace sorafix_api.Services
{
    public class TelegramBotService : BackgroundService
    {
        private readonly TelegramBotClient _botClient;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TelegramBotService> _logger;

        public TelegramBotService(
            IConfiguration config,
            IServiceScopeFactory scopeFactory,
            IMemoryCache cache,
            ILogger<TelegramBotService> logger)
        {
            var token = config["Telegram:Token"] ?? throw new ArgumentNullException("Telegram token is missing");
            _botClient = new TelegramBotClient(token);
            _scopeFactory = scopeFactory;
            _cache = cache;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(2000, stoppingToken);

                var me = await _botClient.GetMe(stoppingToken);
                _logger.LogInformation($"Бот {me.Username} успешно запущен.");

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
                };

                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Критическая ошибка при запуске бота: {ex.Message}");
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackQuery(botClient, update.CallbackQuery, cancellationToken);
                return;
            }

            if (update.Message is not { Text: { } messageText } message) return;

            var chatId = message.Chat.Id;

            if (messageText.StartsWith("/start"))
            {
                var parts = messageText.Split(' ');
                if (parts.Length > 1)
                {
                    var code = parts[1]; 

                    if (_cache.TryGetValue($"tg_code_{code}", out int userId))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<SorafixContext>();
                        var user = await dbContext.Users.FindAsync(new object[] { userId }, cancellationToken);

                        if (user != null)
                        {
                            user.TgChatId = chatId;
                            await dbContext.SaveChangesAsync(cancellationToken);
                            _cache.Remove($"tg_code_{code}");

                            await botClient.SendMessage(chatId, $"Здравствуйте, {user.FirstName}! Ваш аккаунт успешно привязан.", cancellationToken: cancellationToken);
                            await ShowMainMenu(botClient, chatId, null, cancellationToken);
                            return;
                        }
                    }
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SorafixContext>();
                    var isLinked = await db.Users.AnyAsync(u => u.TgChatId == chatId);

                    if (isLinked)
                    {
                        await ShowMainMenu(botClient, chatId, null, cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "Здравствуйте! Чтобы привязать аккаунт, перейди по ссылке из личного кабинета SORAFIX.", cancellationToken: cancellationToken);
                    }
                }
            }
        }

        private async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, int? messageId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📦 Мои заявки", "my_requests") },
                new[] { InlineKeyboardButton.WithCallbackData("🛠️ Поддержка", "support") }
            });

            string text = "Панель управления SORAFIX:";

            if (messageId.HasValue)
            {
                await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId.Value,
                    text: text,
                    replyMarkup: keyboard,
                    cancellationToken: ct);
            }
            else
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    replyMarkup: keyboard,
                    cancellationToken: ct);
            }
        }

        private async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery query, CancellationToken ct)
        {
            if(query.Message == null) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SorafixContext>();

            var currentChatId = query.Message.Chat.Id;
            _logger.LogInformation($"Нажата кнопка. Callback: {query.Data}, ChatId: {currentChatId}");

            var user = await db.Users.FirstOrDefaultAsync(u => u.TgChatId == currentChatId);

            if (user == null)
            {
                await botClient.AnswerCallbackQuery(query.Id, "Аккаунт не найден в базе.", cancellationToken: ct);
                return;
            }

            if (query.Data == "my_requests")
            {
                var requests = await db.Requests
                    .Where(r => r.ClientId == user.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                if (!requests.Any())
                {
                    await botClient.AnswerCallbackQuery(query.Id, "У вас пока нет активных заявок.", cancellationToken: ct);
                    return;
                }

                var buttons = requests.Select(r => new[] { InlineKeyboardButton.WithCallbackData($"№{r.Id} - {r.Title}", $"req_{r.Id}") }).ToList();
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 Назад", "main_menu") });

                await botClient.EditMessageText(
                    chatId: currentChatId,
                    messageId: query.Message.MessageId,
                    text: "📋 Ваши последние заявки:",
                    replyMarkup: new InlineKeyboardMarkup(buttons),
                    cancellationToken: ct);
            }
            else if (query.Data.StartsWith("req_"))
            {
                int reqId = int.Parse(query.Data.Split('_')[1]);
                var req = await db.Requests.FirstOrDefaultAsync(r => r.Id == reqId);

                string GetStatusName(int statusId) => statusId switch
                {
                    1 => "Новая",
                    2 => "Ожидание",
                    3 => "В работе",
                    4 => "Проверка",
                    5 => "Готова",
                    6 => "Закрыта",
                    7 => "Отменена",
                    _ => "Неизвестно"
                };

                string info =
                    $"📦 *Заявка №{req.Id}*\n\n" +
                    $"🧾 *Название:* {req.Title}\n" +
                    $"📝 *Описание:* {req.Description ?? "—"}\n\n" +
                    $"💰 *Стоимость:* {req.Price:0.00} руб.\n" +
                    $"📌 *Статус:* {GetStatusName(req.StatusId)}\n\n" +
                    $"🕒 *Обновлено:* {req.UpdatedAt:dd.MM.yyyy HH:mm}";

                var backBtn = new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("🔙 К списку", "my_requests") });
                await botClient.EditMessageText(query.Message.Chat.Id, query.Message.MessageId, info, parseMode: ParseMode.Markdown, replyMarkup: backBtn, cancellationToken: ct);
            }
            else if (query.Data == "support")
            {
                string supportInfo = "🛠️ *Техническая поддержка*\n\n📩 Email: support@sorafix.ru\n☎️ Телефон: +7 (800) 555-35-35\n\nЧасы работы: Пн-Пт с 9:00 до 18:00";
                var backBtn = new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("🔙 Назад", "main_menu") });
                await botClient.EditMessageText(query.Message.Chat.Id, query.Message.MessageId, supportInfo, parseMode: ParseMode.Markdown, replyMarkup: backBtn, cancellationToken: ct);
            }
            else if (query.Data == "main_menu")
            {
                await ShowMainMenu(botClient, query.Message.Chat.Id, query.Message.MessageId, ct);
            }

            await botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            _logger.LogError(errorMessage);
            return Task.CompletedTask;
        }
    }
}