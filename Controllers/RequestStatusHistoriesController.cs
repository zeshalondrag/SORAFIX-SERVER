using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class RequestStatusHistoriesController : ControllerBase
    {
        private readonly SorafixContext _context;

        public RequestStatusHistoriesController(SorafixContext context)
        {
            _context = context;
        }

        // GET: api/RequestStatusHistories
        [HttpGet]
        public async Task<ActionResult> GetRequestStatusHistories()
        {
            var history = await _context.RequestStatusHistories
                .AsNoTracking()
                .OrderBy(h => h.ChangedAt)
                .Select(h => new
                {
                    h.Id,
                    h.RequestId,
                    h.StatusId,
                    StatusName = h.Status.Name,
                    h.ChangedBy,
                    ChangedByName = $"{h.ChangedByNavigation.LastName} {h.ChangedByNavigation.FirstName}",
                    h.ChangedAt
                })
                .ToListAsync();

            return Ok(history);
        }

        // GET: api/RequestStatusHistories/request/5
        [HttpGet("request/{requestId}")]
        public async Task<ActionResult> GetHistoryByRequest(int requestId)
        {
            if (!await _context.Requests.AnyAsync(r => r.Id == requestId))
                return NotFound("Заявка не найдена");

            var history = await _context.RequestStatusHistories
                .AsNoTracking()
                .Where(h => h.RequestId == requestId)
                .Include(h => h.Status)
                .Include(h => h.ChangedByNavigation)
                .OrderBy(h => h.ChangedAt)
                .Select(h => new
                {
                    h.Id,
                    h.StatusId,
                    StatusName = h.Status.Name,
                    StatusDescription = h.Status.Description,
                    ChangedByName = $"{h.ChangedByNavigation.LastName} {h.ChangedByNavigation.FirstName}",
                    h.ChangedAt
                })
                .ToListAsync();

            return Ok(history);
        }

        // GET: api/RequestStatusHistories/5
        [HttpGet("{id}")]
        public async Task<ActionResult> GetRequestStatusHistory(int id)
        {
            var entry = await _context.RequestStatusHistories
                .AsNoTracking()
                .Include(h => h.Status)
                .Include(h => h.ChangedByNavigation)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (entry == null) return NotFound();

            return Ok(new
            {
                entry.Id,
                entry.RequestId,
                StatusName = entry.Status.Name,
                ChangedByName = $"{entry.ChangedByNavigation.LastName} {entry.ChangedByNavigation.FirstName}",
                entry.ChangedAt
            });
        }
    }
}