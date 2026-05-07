using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;
using sorafix_api.Models.DTO;
using sorafix_api.Services;
using System.Security.Claims;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class ChatMessagesController : ControllerBase
    {
        private readonly SorafixContext _context;
        private readonly IConfiguration _configuration;
        private readonly INotificationService _notificationService;

        public ChatMessagesController(SorafixContext context, IConfiguration configuration, INotificationService notificationService)
        {
            _context = context;
            _configuration = configuration;
            _notificationService = notificationService;
        }

        private int GetCurrentUserId() =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        [HttpGet("request/{requestId}")]
        public async Task<ActionResult<IEnumerable<CommentResponse>>> GetChatMessages(int requestId)
        {
            var messages = await _context.ChatMessages
                .AsNoTracking()
                .Where(m => m.RequestId == requestId)
                .Include(m => m.User)
                .Include(m => m.Attachments)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new CommentResponse
                {
                    Id = m.Id,
                    RequestId = m.RequestId,
                    Text = m.MessageText ?? "",
                    CreatedAt = m.CreatedAt,
                    UserId = m.UserId,
                    FirstName = m.User.FirstName,
                    LastName = m.User.LastName,
                    RoleName = m.User.Role.Name,
                    Attachments = m.Attachments.Select(a => new ChatAttachmentResponse
                    {
                        FilePath = a.FilePath,
                        OriginalName = a.OriginalName,
                        FileType = a.FileType
                    }).ToList(),
                    UpdatedAt = m.UpdatedAt,
                    IsEdited = m.IsEdited,
                })
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost]
        public async Task<ActionResult> PostChatMessage(CreateComment dto)
        {
            var userId = GetCurrentUserId();

            var message = new ChatMessage
            {
                RequestId = dto.RequestId,
                UserId = userId,
                MessageText = dto.Text,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsEdited = false
            };

            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();

            var request = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RequestId);
            if (request != null)
            {
                int recipientId = (userId == request.ClientId) ? (request.EmployeeId ?? 0) : request.ClientId;
                if (recipientId != 0)
                {
                    await _notificationService.SendTelegramNotificationAsync(recipientId,
                        $"💬 *Новое сообщение по заявке №{dto.RequestId}*:\n_{dto.Text}_");
                }
            }

            return Ok(new { message.Id, message.CreatedAt });
        }

        [HttpPost("{requestId}/upload")]
        public async Task<IActionResult> UploadChatFile(int requestId, IFormFile file)
        {
            var userId = GetCurrentUserId();
            if (file == null || file.Length == 0) return BadRequest("Файл пуст");

            var allowedTypes = new[] { "image/jpeg", "image/png", "application/pdf" };
            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest("Допустимы только JPEG, PNG и PDF");

            try
            {
                var message = new ChatMessage
                {
                    RequestId = requestId,
                    UserId = userId,
                    MessageText = file.ContentType.Contains("image") ? "[Фотография]" : "[Документ]",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ChatMessages.Add(message);
                await _context.SaveChangesAsync();

                var fileExt = Path.GetExtension(file.FileName);
                var s3Key = $"chat/{requestId}/{Guid.NewGuid()}{fileExt}";
                var publicUrl = await UploadToS3(file, s3Key);

                var attachment = new Attachment
                {
                    RequestId = requestId,
                    MessageId = message.Id, 
                    UploadedBy = userId,
                    FilePath = publicUrl,
                    OriginalName = file.FileName,
                    FileType = file.ContentType,
                    FileSize = (int)file.Length,
                    AttachmentType = "chat_file",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Attachments.Add(attachment);
                await _context.SaveChangesAsync();

                return Ok(new { message.Id, filePath = publicUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при загрузке: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutChatMessage(int id, UpdateComment dto)
        {
            var message = await _context.ChatMessages.FindAsync(id);
            if (message == null) return NotFound();

            if (message.UserId != GetCurrentUserId()) return Forbid();

            message.MessageText = dto.Text;
            message.IsEdited = true;
            message.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteChatMessage(int id)
        {
            var message = await _context.ChatMessages
                .Include(m => m.Attachments)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (message == null) return NotFound();

            if (message.UserId != GetCurrentUserId())
                return Forbid();

            if (message.Attachments.Any())
            {
                _context.Attachments.RemoveRange(message.Attachments);
            }

            _context.ChatMessages.Remove(message);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task<string> UploadToS3(IFormFile file, string key)
        {
            var s3Config = new AmazonS3Config { ServiceURL = _configuration["YandexCloud:ServiceUrl"] };
            using var client = new AmazonS3Client(_configuration["YandexCloud:AccessKey"], _configuration["YandexCloud:SecretKey"], s3Config);

            using var stream = file.OpenReadStream();
            var putRequest = new PutObjectRequest
            {
                BucketName = _configuration["YandexCloud:BucketName"],
                Key = key,
                InputStream = stream,
                ContentType = file.ContentType,
                CannedACL = S3CannedACL.PublicRead
            };

            await client.PutObjectAsync(putRequest);
            return $"https://storage.yandexcloud.net/{_configuration["YandexCloud:BucketName"]}/{key}";
        }
    }
}