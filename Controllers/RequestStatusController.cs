using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class RequestStatusController : ControllerBase
    {
        private readonly SorafixContext _context;

        public RequestStatusController(SorafixContext context)
        {
            _context = context;
        }

        // GET: api/RequestStatus
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RequestStatus>>> GetRequestStatuses()
        {
            return await _context.RequestStatuses.ToListAsync();
        }

        // GET: api/RequestStatus/5
        [HttpGet("{id}")]
        public async Task<ActionResult<RequestStatus>> GetRequestStatus(int id)
        {
            var requestStatus = await _context.RequestStatuses.FindAsync(id);

            if (requestStatus == null)
            {
                return NotFound();
            }

            return requestStatus;
        }
    }
}