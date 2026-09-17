using Google.Cloud.Firestore;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly FirestoreDb _db;

        public NotificationsController(FirestoreDb db)
        {
            _db = db;
        }

        private Task<string?> CallerUidAsync(string? q = null) => ApiUserContext.RequireUserIdAsync(HttpContext, q);

        private IActionResult NeedAuth() =>
            Unauthorized(new { error = "Missing user identity. Send Authorization: Bearer <FirebaseIdToken>, X-User-Id header, or ?userId=." });

        private static NotificationDto ToDto(DocumentSnapshot doc)
        {
            var data = doc.ToDictionary();
            var id = data.TryGetValue("Id", out var rawId) && !string.IsNullOrWhiteSpace(rawId?.ToString()) ? rawId!.ToString()! : doc.Id;
            return new NotificationDto
            {
                Id = id,
                Type = data.TryGetValue("Type", out var t) ? t?.ToString() ?? "" : "",
                Message = data.TryGetValue("Message", out var m) ? m?.ToString() ?? "" : "",
                CreatedAt = data.TryGetValue("CreatedAt", out var c) ? c?.ToString() ?? "" : "",
                IsRead = data.TryGetValue("IsRead", out var r) && r is bool b && b,
                MeetupId = data.TryGetValue("MeetupId", out var mi) ? mi?.ToString() : null,
                FriendRequestId = data.TryGetValue("FriendRequestId", out var fr) ? fr?.ToString() : null,
                GroupId = data.TryGetValue("GroupId", out var gr) ? gr?.ToString() : null
            };
        }

        // GET /api/notifications?userId={uid}
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var snap = await _db.Collection("users").Document(uid).Collection("notifications")
                .OrderByDescending("CreatedAt").Limit(50).GetSnapshotAsync();
            return Ok(snap.Documents.Select(ToDto).ToList());
        }

        // GET /api/notifications/unread-count?userId={uid}
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var snap = await _db.Collection("users").Document(uid).Collection("notifications")
                .WhereEqualTo("IsRead", false).GetSnapshotAsync();
            return Ok(new { count = snap.Count });
        }

        // POST /api/notifications/{id}/read?userId={uid}
        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            await _db.Collection("users").Document(uid).Collection("notifications").Document(id).UpdateAsync("IsRead", true);
            return Ok(new { message = "Marked as read." });
        }

        // POST /api/notifications/read-all?userId={uid}
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var snap = await _db.Collection("users").Document(uid).Collection("notifications")
                .WhereEqualTo("IsRead", false).GetSnapshotAsync();
            var batch = _db.StartBatch();
            foreach (var d in snap.Documents) batch.Update(d.Reference, "IsRead", true);
            await batch.CommitAsync();
            return Ok(new { message = $"Marked {snap.Count} notification(s) as read." });
        }

        // DELETE /api/notifications/{id}?userId={uid}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();
            await _db.Collection("users").Document(uid).Collection("notifications").Document(id).DeleteAsync();
            return Ok(new { message = "Notification deleted." });
        }
    }
}
