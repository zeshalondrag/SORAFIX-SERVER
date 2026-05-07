using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using sorafix_api.Models;
using sorafix_api.Models.DTO;
using sorafix_api.Services;
using System.Security.Claims;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class RequestsController : ControllerBase
    {
        private readonly SorafixContext _context;
        private readonly IConfiguration _configuration;
        private readonly INotificationService _notificationService;

        public RequestsController(SorafixContext context, IConfiguration configuration, INotificationService notificationService)
        {
            _context = context;
            _configuration = configuration;
            _notificationService = notificationService;
        } 

        private int? GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }

        private string? GetCurrentUserRole() => User.FindFirst(ClaimTypes.Role)?.Value;

        private async Task AddNotificationAsync(int userId, int requestId, string message)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = userId,
                RequestId = requestId,
                Title = "Обновление по заявке",
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task ChangeStatusAsync(Request request, int newStatusId, int changedBy)
        {
            request.StatusId = newStatusId;
            request.UpdatedAt = DateTime.UtcNow;

            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                RequestId = request.Id,
                StatusId = newStatusId,
                ChangedBy = changedBy,
                ChangedAt = DateTime.UtcNow
            });
        }

        private async Task TrySetWaitingStatus(Request request, int managerId)
        {
            if (request.StatusId == 1 && request.IsPriceConfirmed && request.EmployeeId.HasValue)
            {
                await ChangeStatusAsync(request, 2, managerId);
                await AddNotificationAsync(request.ClientId, request.Id, $"Заявка \"{request.Title}\" перешла в статус \"Ожидание\". Ожидается принятие специалистом.");
                await AddNotificationAsync(request.EmployeeId.Value, request.Id, $"Вам назначена новая заявка \"{request.Title}\"");
            }
        }

        // GET: api/Requests
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Request>>> GetRequests()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();
            var role = GetCurrentUserRole();

            var query = _context.Requests.AsNoTracking().AsQueryable();

            if (role == "Клиент")
                query = query.Where(r => r.ClientId == userId.Value);
            else if (role == "Технический специалист")
                query = query.Where(r => r.EmployeeId == userId.Value);

            return await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        }

        // GET: api/Requests/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Request>> GetRequest(int id)
        {
            var request = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (request == null) return NotFound();

            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();

            if (role == "Клиент" && request.ClientId != userId) return Forbid();
            if (role == "Технический специалист" && request.EmployeeId != userId) return Forbid();

            return request;
        }

        // POST: api/Requests
        [HttpPost]
        public async Task<ActionResult<Request>> PostRequest([FromBody] CreateRequest dto)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = new Request
            {
                ClientId = userId.Value,
                RequestTypeId = dto.RequestTypeId,
                Title = dto.Title,
                Description = dto.Description,
                StatusId = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsPriceConfirmed = false
            };

            _context.Requests.Add(request);
            await _context.SaveChangesAsync();

            await ChangeStatusAsync(request, 1, userId.Value);
            await AddNotificationAsync(userId.Value, request.Id, $"Заявка \"{request.Title}\" успешно создана");

            var managers = await _context.Users.AsNoTracking().Where(u => u.RoleId == 2 && u.IsActive).ToListAsync();
            foreach (var manager in managers)
            {
                await AddNotificationAsync(manager.Id, request.Id, $"Новая заявка \"{request.Title}\" от клиента");
            }

            await _context.SaveChangesAsync();
            return CreatedAtAction("GetRequest", new { id = request.Id }, request);
        }

        // POST: api/Requests/5/upload-images
        [HttpPost("{requestId}/upload-images")]
        public async Task<IActionResult> UploadRequestImages(int requestId, [FromForm] List<IFormFile> files)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            if (files == null || files.Count == 0)
                return BadRequest("Файлы не выбраны.");

            if (files.Count > 3)
                return BadRequest("Нельзя загрузить более 3-х фотографий одновременно.");

            var request = await _context.Requests.AnyAsync(r => r.Id == requestId);
            if (!request) return NotFound("Заявка не найдена.");

            var existingPhotosCount = await _context.Attachments
                .CountAsync(a => a.RequestId == requestId && a.AttachmentType == "request_initial");

            if (existingPhotosCount + files.Count > 3)
                return BadRequest($"У этой заявки уже есть {existingPhotosCount} фото. Вы можете добавить еще {3 - existingPhotosCount}.");

            var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            var uploadedAttachments = new List<Attachment>();

            try
            {
                foreach (var file in files)
                {
                    if (!allowedMimeTypes.Contains(file.ContentType))
                        return BadRequest($"Файл {file.FileName} имеет недопустимый формат.");

                    if (file.Length > 15728640)
                        return BadRequest($"Файл {file.FileName} слишком большой.");

                    var fileExt = Path.GetExtension(file.FileName);
                    var uniqueName = $"{Guid.NewGuid():N}{fileExt}";
                    var s3Key = $"requests/{requestId}/images/{uniqueName}";

                    string publicUrl;
                    using (var stream = file.OpenReadStream())
                    {
                        var bytes = new byte[file.Length];
                        await stream.ReadAsync(bytes, 0, (int)file.Length);
                        publicUrl = await UploadToS3Async(bytes, s3Key, file.ContentType);
                    }

                    var attachment = new Attachment
                    {
                        RequestId = requestId,
                        UploadedBy = userId.Value,
                        FilePath = publicUrl,
                        OriginalName = file.FileName,
                        FileType = file.ContentType,
                        FileSize = (int)file.Length,
                        AttachmentType = "request_initial",
                        CreatedAt = DateTime.UtcNow
                    };

                    uploadedAttachments.Add(attachment);
                }

                _context.Attachments.AddRange(uploadedAttachments);
                await _context.SaveChangesAsync();

                return Ok(uploadedAttachments);
            }
            catch (AmazonS3Exception e)
            {
                return StatusCode(500, $"Ошибка Yandex Cloud: {e.Message}");
            }
            catch (Exception e)
            {
                return StatusCode(500, $"Внутренняя ошибка: {e.Message}");
            }
        }

        // PATCH: api/Requests/5/price
        [HttpPatch("{id}/price")]
        public async Task<IActionResult> UpdatePrice(int id, [FromBody] UpdatePrice dto)
        {
            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            request.Price = dto.Price;
            request.IsPriceConfirmed = false;
            request.UpdatedAt = DateTime.UtcNow;

            await AddNotificationAsync(request.ClientId, request.Id, $"По заявке \"{request.Title}\" выставлена цена: {dto.Price} руб. Требуется ваше подтверждение.");

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/Requests/5/confirm-price
        [HttpPatch("{id}/confirm-price")]
        public async Task<IActionResult> ConfirmPrice(int id, [FromBody] ConfirmPrice dto)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            if (dto.IsConfirmed)
            {
                request.IsPriceConfirmed = true;
                request.UpdatedAt = DateTime.UtcNow;

                await TrySetWaitingStatus(request, userId.Value);

                if (request.EmployeeId.HasValue)
                {
                    await AddNotificationAsync(request.EmployeeId.Value, request.Id,
                        $"Клиент подтвердил цену по заявке \"{request.Title}\"");
                }
                else
                {
                    var managers = await _context.Users.Where(u => u.RoleId == 2 && u.IsActive).ToListAsync();
                    foreach (var manager in managers)
                    {
                        await AddNotificationAsync(manager.Id, request.Id,
                            $"Клиент подтвердил цену {request.Price} руб. по заявке \"{request.Title}\". Назначьте специалиста.");
                    }
                }
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/Requests/5/assign
        [HttpPatch("{id}/assign")]
        public async Task<IActionResult> AssignEmployee(int id, [FromBody] AssignEmployee dto)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            request.EmployeeId = dto.EmployeeId;
            request.UpdatedAt = DateTime.UtcNow;

            await TrySetWaitingStatus(request, userId.Value);

            await _context.SaveChangesAsync();
            return NoContent();
        }

        #region Управление статусами

        // PATCH: api/Requests/5/accept
        [HttpPatch("{id}/accept")]
        public async Task<IActionResult> AcceptRequest(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            if (request.EmployeeId != userId.Value) return Forbid();
            if (request.StatusId != 2) return BadRequest("Принять можно только заявку в статусе 'Ожидание'");

            await ChangeStatusAsync(request, 3, userId.Value);
            string msg = $"⚙️ Специалист начал работу над заявкой \"{request.Title}\" (Статус: В работе)";
            await AddNotificationAsync(request.ClientId, request.Id, msg);

            await _context.SaveChangesAsync();
            await _notificationService.SendTelegramNotificationAsync(request.ClientId, msg);
            return NoContent();
        }

        // PATCH: api/Requests/5/complete
        [HttpPatch("{id}/complete")]
        public async Task<IActionResult> CompleteRequest(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            if (request.EmployeeId != userId.Value) return Forbid();
            if (request.StatusId != 3) return BadRequest("Завершить можно только заявку в статусе 'В работе'");

            await ChangeStatusAsync(request, 4, userId.Value);

            var managers = await _context.Users.AsNoTracking().Where(u => u.RoleId == 2 && u.IsActive).ToListAsync();
            foreach (var manager in managers)
                await AddNotificationAsync(manager.Id, request.Id, $"Заявка \"{request.Title}\" выполнена специалистом и ожидает вашей проверки.");

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/Requests/5/verify
        [HttpPatch("{id}/verify")]
        public async Task<IActionResult> VerifyRequest(int id, [FromBody] VerifyRequest dto)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            if (request.StatusId != 4) return BadRequest("Проверять можно только заявку в статусе 'Проверка'");

            string? telegramMsg = null;
            int? recipientId = null;

            if (dto.IsApproved)
            {
                await ChangeStatusAsync(request, 5, userId.Value);
                telegramMsg = $"✅ Ваша заявка \"{request.Title}\" готова к выдаче!";
                recipientId = request.ClientId;

                await AddNotificationAsync(request.ClientId, request.Id, telegramMsg);
            }
            else
            {
                await ChangeStatusAsync(request, 3, userId.Value);
                telegramMsg = $"⚠️ Менеджер отклонил выполнение заявки \"{request.Title}\". Заявка возвращена в работу.";
                recipientId = request.ClientId;

                await AddNotificationAsync(request.ClientId, request.Id, telegramMsg);
            }

            await _context.SaveChangesAsync();
            Console.WriteLine($"Попытка отправить уведомление пользователю ID: {recipientId} сообщение: {telegramMsg}");
            if (recipientId.HasValue && !string.IsNullOrEmpty(telegramMsg))
            {
                Console.WriteLine($"Попытка отправить уведомление пользователю ID: {recipientId} сообщение: {telegramMsg}");
                await _notificationService.SendTelegramNotificationAsync(recipientId.Value, telegramMsg);
            }
            return NoContent();
        }

        // PATCH: api/Requests/5/close
        [HttpPatch("{id}/close")]
        public async Task<IActionResult> CloseRequest(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            if (request.StatusId != 5) return BadRequest("Закрыть можно только заявку со статусом 'Готова'");

            await ChangeStatusAsync(request, 6, userId.Value);
            request.ClosedAt = DateTime.UtcNow;

            await AddNotificationAsync(request.ClientId, request.Id, $"Заявка \"{request.Title}\" закрыта. Спасибо, что выбрали нас!");

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/Requests/5/cancel
        [HttpPatch("{id}/cancel")]
        public async Task<IActionResult> CancelRequest(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();
            var role = GetCurrentUserRole();

            var request = await _context.Requests.FindAsync(id);
            if (request == null) return NotFound();

            if (request.StatusId == 7) return BadRequest("Заявка уже отменена");
            if (request.StatusId == 6) return BadRequest("Нельзя отменить закрытую заявку");

            if (role == "Клиент")
            {
                if (request.StatusId != 1) return BadRequest("Клиент может отменить заявку только со статусом 'Новая'");
                if (request.ClientId != userId.Value) return Forbid();
            }

            await ChangeStatusAsync(request, 7, userId.Value);
            request.ClosedAt = DateTime.UtcNow;

            if (role != "Клиент")
                await AddNotificationAsync(request.ClientId, request.Id, $"Ваша заявка \"{request.Title}\" была отменена.");

            await _context.SaveChangesAsync();
            return NoContent();
        }

        #endregion

        // POST: api/Requests/5/generate-contract
        [HttpPost("{id}/generate-contract")]
        public async Task<IActionResult> GenerateContract(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Unauthorized();

            var request = await _context.Requests
                .Include(r => r.Client)
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null) return NotFound();

            if (request.Price <= 0 || !request.IsPriceConfirmed)
                return BadRequest("Цена должна быть установлена и подтверждена клиентом.");

            if (request.Employee == null || request.Employee.RoleId != 3) 
                return BadRequest("Должен быть назначен сотрудник с ролью 'Технический специалист'.");

            try
            {
                var pdfBytes = GeneratePdfBytes(request);

                var uniqueName = $"contract_{id}_{Guid.NewGuid():N}.pdf";
                var s3Key = $"requests/{id}/{uniqueName}";
                var publicUrl = await UploadToS3Async(pdfBytes, s3Key, "application/pdf");

                var attachment = new Attachment
                {
                    RequestId = id,
                    UploadedBy = userId.Value,
                    FilePath = publicUrl,
                    OriginalName = $"Договор заявки №{id}.pdf",
                    FileType = "application/pdf",
                    FileSize = pdfBytes.Length,
                    AttachmentType = "contract", 
                    CreatedAt = DateTime.UtcNow
                };

                _context.Attachments.Add(attachment);
                await _context.SaveChangesAsync();

                return Ok(new { Url = publicUrl, AttachmentId = attachment.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при генерации или сохранении договора: {ex.Message}");
            }
        }

        private byte[] GeneratePdfBytes(Request req)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial));

                    page.Header().Text("ДОГОВОР НА ОКАЗАНИЕ ТЕХНИЧЕСКИХ УСЛУГ")
                        .SemiBold().FontSize(16).FontColor(Colors.Black).AlignCenter();

                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text($"г.Москва \t\t\t\t\t\t Дата: {DateTime.UtcNow:dd.MM.yyyy}");

                        col.Item().Text(text =>
                        {
                            text.Span("Сервисный центр \"SORAFIX\", именуемый в дальнейшем \"Исполнитель\", и гражданин(ка) ");
                            text.Span($"{req.Client.LastName} {req.Client.FirstName}").SemiBold();
                            text.Span(", именуемый в дальнейшем \"Заказчик\", заключили настоящий договор о нижеследующем:");
                        });

                        col.Item().PaddingTop(10).Text("1. Предмет договора").SemiBold().FontSize(12);
                        col.Item().Text("Исполнитель обязуется по заданию Заказчика оказать технические услуги (диагностика, ремонт, настройка ПО), а Заказчик обязуется принять и оплатить эти услуги.");

                        col.Item().PaddingTop(10).Text("2. Данные заявки").SemiBold().FontSize(12);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Border(1).Padding(5).Text("Параметр").SemiBold();
                                header.Cell().Border(1).Padding(5).Text("Значение").SemiBold();
                            });

                            void AddRow(string label, string value)
                            {
                                table.Cell().Border(1).Padding(5).Text(label);
                                table.Cell().Border(1).Padding(5).Text(value);
                            }

                            AddRow("Номер заявки", req.Id.ToString());
                            AddRow("Наименование", req.Title);
                            AddRow("Описание проблемы", req.Description ?? "Не указано");
                            AddRow("Тех. специалист", $"{req.Employee.LastName} {req.Employee.FirstName}");
                            AddRow("Стоимость услуг", $"{req.Price} руб.");
                            AddRow("Дата обращения", req.CreatedAt.ToString("dd.MM.yyyy HH:mm"));
                        });

                        col.Item().PaddingTop(10).Text("3. Финансы и Сроки").SemiBold().FontSize(12);
                        col.Item().Text($"3.1. Стоимость услуг составляет {req.Price} руб. Оплата производится Заказчиком после проверки выполненных работ.");
                        col.Item().Text("3.2. Срок выполнения работ определяется тех. специалистом после диагностики.");

                        col.Item().PaddingTop(10).Text("4. Права, обязанности и ответственность").SemiBold().FontSize(12);
                        col.Item().Text("4.1. Исполнитель обязуется выполнить работу качественно. Заказчик обязуется предоставить оборудование и оплатить услуги.");
                        col.Item().Text("4.2. Исполнитель не несет ответственности за потерю пользовательских данных (фото, документы), если Заказчик не сделал резервную копию до передачи устройства.");

                        col.Item().PaddingTop(10).Text("5. Гарантия и Прочее").SemiBold().FontSize(12);
                        col.Item().Text("5.1. На все выполненные аппаратные работы предоставляется гарантия 30 дней. На программные сбои по вине пользователя гарантия не распространяется.");
                        col.Item().Text("5.2. Дополнительные работы, выявленные в ходе ремонта, согласовываются с Заказчиком отдельно.");

                        col.Item().PaddingTop(30).Text("Реквизиты и подписи сторон:").SemiBold().FontSize(12);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); });
                            table.Cell().Text("ИСПОЛНИТЕЛЬ:\nООО \"SORAFIX\"\n\n_________________ (Подпись)");
                            table.Cell().Text($"ЗАКАЗЧИК:\n{req.Client.LastName} {req.Client.FirstName}\n\n_________________ (Подпись)");
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Страница ");
                        x.CurrentPageNumber();
                        x.Span(" из ");
                        x.TotalPages();
                    });
                });
            });

            return document.GeneratePdf();
        }

        private async Task<string> UploadToS3Async(byte[] fileBytes, string s3Key, string contentType)
        {
            var s3Config = new AmazonS3Config { ServiceURL = _configuration["YandexCloud:ServiceUrl"] };
            var accessKey = _configuration["YandexCloud:AccessKey"];
            var secretKey = _configuration["YandexCloud:SecretKey"];
            var bucketName = _configuration["YandexCloud:BucketName"];

            using var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
            using var stream = new MemoryStream(fileBytes);

            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = s3Key,
                InputStream = stream,
                ContentType = contentType,
                CannedACL = S3CannedACL.PublicRead
            };

            await s3Client.PutObjectAsync(putRequest);
            return $"https://storage.yandexcloud.net/{bucketName}/{s3Key}";
        }
    }
}