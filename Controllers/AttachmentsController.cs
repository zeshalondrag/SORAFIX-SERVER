using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class AttachmentsController : ControllerBase
    {
        private readonly SorafixContext _context;
        private readonly IConfiguration _configuration;

        public AttachmentsController(SorafixContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // GET: api/Attachments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Attachment>>> GetAttachments()
        {
            return await _context.Attachments.ToListAsync();
        }

        // GET: api/Attachments/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Attachment>> GetAttachment(int id)
        {
            var attachment = await _context.Attachments.FindAsync(id);

            if (attachment == null)
            {
                return NotFound();
            }

            return attachment;
        }

        // GET: api/Attachments/request/5
        [HttpGet("request/{requestId}")]
        public async Task<ActionResult<IEnumerable<Attachment>>> GetRequestPhotos(int requestId)
        {
            return await _context.Attachments
                .Where(p => p.RequestId == requestId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        // DELETE: api/Attachments/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var attachment = await _context.Attachments.FindAsync(id);
            if (attachment == null)
            {
                return NotFound(new { message = "Файл не найден" });
            }

            try
            {
                string s3Key = ExtractKeyFromUrl(attachment.FilePath);
                await DeleteFromS3Async(s3Key);

                _context.Attachments.Remove(attachment);
                await _context.SaveChangesAsync();

                return NoContent(); 
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Ошибка при удалении", error = ex.Message });
            }
        }

        private async Task DeleteFromS3Async(string s3Key)
        {
            var s3Config = new AmazonS3Config { ServiceURL = _configuration["YandexCloud:ServiceUrl"] };
            var accessKey = _configuration["YandexCloud:AccessKey"];
            var secretKey = _configuration["YandexCloud:SecretKey"];
            var bucketName = _configuration["YandexCloud:BucketName"];

            using var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = s3Key
            };

            await s3Client.DeleteObjectAsync(deleteRequest);
        }

        private string ExtractKeyFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;

            var bucketName = _configuration["YandexCloud:BucketName"];
            string prefix = $"https://storage.yandexcloud.net/{bucketName}/";

            if (url.StartsWith(prefix))
            {
                return url.Replace(prefix, "");
            }

            return Path.GetFileName(url);
        }
    }
}