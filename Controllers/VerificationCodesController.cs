using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sorafix_api.Models;

namespace sorafix_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class VerificationCodesController : ControllerBase
    {
        private readonly SorafixContext _context;

        public VerificationCodesController(SorafixContext context)
        {
            _context = context;
        }

        // GET: api/VerificationCodes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<VerificationCode>>> GetVerificationCodes()
        {
            return await _context.VerificationCodes.ToListAsync();
        }

        // GET: api/VerificationCodes/5
        [HttpGet("{id}")]
        public async Task<ActionResult<VerificationCode>> GetVerificationCode(int id)
        {
            var verificationCode = await _context.VerificationCodes.FindAsync(id);

            if (verificationCode == null)
            {
                return NotFound();
            }

            return verificationCode;
        }

        // POST: api/VerificationCodes
        [HttpPost]
        public async Task<ActionResult<VerificationCode>> PostVerificationCode(VerificationCode verificationCode)
        {
            _context.VerificationCodes.Add(verificationCode);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetVerificationCode", new { id = verificationCode.Id }, verificationCode);
        }
    }
}