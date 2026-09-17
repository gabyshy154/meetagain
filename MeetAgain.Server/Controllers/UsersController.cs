using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly FirestoreDb _db;
        private readonly FirestoreService _fs;

        public UsersController(FirestoreDb db, FirestoreService fs)
        {
            _db = db;
            _fs = fs;
        }

        // GET /api/users
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var users = await _fs.GetAllUsersAsync();
            return Ok(users);
        }

        // GET /api/users/search?email=a@b.com&userId=<callerUid>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string email, [FromQuery] string? userId)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { error = "email query param is required." });

            var (callerId, _) = await ApiUserContext.ResolveAsync(HttpContext, userId);
            if (string.IsNullOrWhiteSpace(callerId))
                return Unauthorized(new { error = "Missing user identity. Send X-User-Id, Bearer token, or ?userId=." });

            var snap = await _db.Collection("users").WhereEqualTo("Email", email).Limit(1).GetSnapshotAsync();
            if (snap.Documents.Count == 0) return NotFound(new { error = "User not found." });

            var doc = snap.Documents[0];
            var user = doc.ConvertTo<AppUser>();
            user.Uid = doc.Id;
            if (user.Uid == callerId) return NotFound(new { error = "User not found." });
            return Ok(user);
        }

        // GET /api/users/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var user = await _fs.GetUserAsync(id);
            if (user == null) return NotFound(new { error = "User not found." });
            return Ok(user);
        }

        // PUT /api/users/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Upsert(string id, [FromBody] UpdateUserRequest req)
        {
            var existing = await _fs.GetUserAsync(id);
            if (existing == null) return NotFound(new { error = "User not found." });

            if (!string.IsNullOrWhiteSpace(req.Email)) existing.Email = req.Email;
            if (!string.IsNullOrWhiteSpace(req.DisplayName)) existing.DisplayName = req.DisplayName;
            await _fs.CreateOrUpdateUserAsync(existing);
            return Ok(existing);
        }
    }
}
