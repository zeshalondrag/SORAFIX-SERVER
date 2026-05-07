using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class RequestTypesController : ControllerBase
    {
        private readonly SorafixContext _context;

        public RequestTypesController(SorafixContext context)
        {
            _context = context;
        }

        // GET: api/RequestTypes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RequestType>>> GetRequestTypes()
        {
            return await _context.RequestTypes.ToListAsync();
        }

        // GET: api/RequestTypes/5
        [HttpGet("{id}")]
        public async Task<ActionResult<RequestType>> GetRequestType(int id)
        {
            var requestType = await _context.RequestTypes.FindAsync(id);

            if (requestType == null)
            {
                return NotFound();
            }

            return requestType;
        }
    }
}