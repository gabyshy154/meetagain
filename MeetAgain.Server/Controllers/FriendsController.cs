using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/friends")]
    public class FriendsController : ControllerBase
    {
        private readonly FirestoreDb _db;

        public FriendsController(FirestoreDb db)
        {
            _db = db;
        }

        private Task<string?> CallerUidAsync(string? userIdQuery = null, string? userIdBody = null)
            => ApiUserContext.RequireUserIdAsync(HttpContext, userIdQuery, userIdBody);

        private IActionResult NeedAuth() =>
            Unauthorized(new { error = "Missing user identity. Send Authorization: Bearer <FirebaseIdToken>, X-User-Id header, or userId in query/body." });

        // GET /api/friends?userId={uid}
        [HttpGet]
        public async Task<IActionResult> GetFriends([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var snap = await _db.Collection("users").Document(uid).Collection("friends").GetSnapshotAsync();
            var friends = snap.Documents.Select(d => d.ConvertTo<Friend>()).OrderBy(f => f.Name).ToList();
            return Ok(friends);
        }

        // GET /api/friends/requests?userId={uid}
        [HttpGet("requests")]
        public async Task<IActionResult> GetRequests([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var snap = await _db.Collection("users").Document(uid).Collection("friendRequests")
                .WhereEqualTo("Status", "pending").GetSnapshotAsync();

            var list = new List<FriendRequest>();
            foreach (var doc in snap.Documents)
            {
                var data = doc.ToDictionary();
                list.Add(new FriendRequest
                {
                    Id = doc.Id,
                    FromUserId = data.TryGetValue("FromUserId", out var a) ? a?.ToString() ?? "" : "",
                    FromUserEmail = data.TryGetValue("FromUserEmail", out var b) ? b?.ToString() ?? "" : "",
                    FromUserName = data.TryGetValue("FromUserName", out var c) ? c?.ToString() ?? "" : "",
                    Status = data.TryGetValue("Status", out var d) ? d?.ToString() ?? "pending" : "pending",
                    SentAt = data.TryGetValue("SentAt", out var e) ? e?.ToString() ?? "" : ""
                });
            }
            return Ok(list);
        }

        // POST /api/friends/requests  { userId, recipientEmail }
        [HttpPost("requests")]
        public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestBody body)
        {
            var uid = await CallerUidAsync(null, body.UserId);
            if (uid == null) return NeedAuth();
            if (string.IsNullOrWhiteSpace(body.RecipientEmail))
                return BadRequest(new { error = "recipientEmail is required." });

            var me = await _db.Collection("users").Document(uid).GetSnapshotAsync();
            if (!me.Exists) return NotFound(new { error = "Caller user document not found." });
            var meData = me.ToDictionary();
            var myEmail = meData.TryGetValue("Email", out var e1) ? e1?.ToString() ?? "" : "";
            var myName = meData.TryGetValue("DisplayName", out var e2) ? e2?.ToString() ?? "" : "";

            var found = await _db.Collection("users").WhereEqualTo("Email", body.RecipientEmail).Limit(1).GetSnapshotAsync();
            if (found.Documents.Count == 0) return NotFound(new { error = "Recipient not found." });
            var recipientId = found.Documents[0].Id;
            if (recipientId == uid) return BadRequest(new { error = "Cannot send friend request to yourself." });

            if ((await _db.Collection("users").Document(uid).Collection("friends").Document(recipientId).GetSnapshotAsync()).Exists)
                return Conflict(new { error = "Already friends." });
            if ((await _db.Collection("users").Document(recipientId).Collection("friendRequests").Document(uid).GetSnapshotAsync()).Exists)
                return Conflict(new { error = "Friend request already sent." });

            await _db.Collection("users").Document(recipientId).Collection("friendRequests").Document(uid)
                .SetAsync(new Dictionary<string, object>
                {
                    { "FromUserId", uid },
                    { "FromUserEmail", myEmail },
                    { "FromUserName", myName },
                    { "Status", "pending" },
                    { "SentAt", DateTime.UtcNow.ToString("o") }
                });

            await CreateNotificationAsync(recipientId, "friend_request", $"{myName} sent you a friend request");
            return Ok(new { message = "Friend request sent.", to = recipientId });
        }

        // POST /api/friends/requests/{fromUserId}/accept  { userId }
        [HttpPost("requests/{fromUserId}/accept")]
        public async Task<IActionResult> Accept(string fromUserId, [FromBody] FriendActionBody? body, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId, body?.UserId);
            if (uid == null) return NeedAuth();

            var reqDoc = await _db.Collection("users").Document(uid).Collection("friendRequests").Document(fromUserId).GetSnapshotAsync();
            if (!reqDoc.Exists) return NotFound(new { error = "Friend request not found." });
            var reqData = reqDoc.ToDictionary();
            var friendName = reqData.TryGetValue("FromUserName", out var n) ? n?.ToString() ?? "" : "";
            var friendEmail = reqData.TryGetValue("FromUserEmail", out var m) ? m?.ToString() ?? "" : "";

            var meDoc = await _db.Collection("users").Document(uid).GetSnapshotAsync();
            var meData = meDoc.ToDictionary();
            var myName = meData.TryGetValue("DisplayName", out var a) ? a?.ToString() ?? "" : "";
            var myEmail = meData.TryGetValue("Email", out var b) ? b?.ToString() ?? "" : "";

            var batch = _db.StartBatch();
            batch.Set(_db.Collection("users").Document(uid).Collection("friends").Document(fromUserId),
                new Dictionary<string, object> { { "Id", fromUserId }, { "Name", friendName }, { "Email", friendEmail }, { "AddedAt", DateTime.UtcNow.ToString("o") } });
            batch.Set(_db.Collection("users").Document(fromUserId).Collection("friends").Document(uid),
                new Dictionary<string, object> { { "Id", uid }, { "Name", myName }, { "Email", myEmail }, { "AddedAt", DateTime.UtcNow.ToString("o") } });
            batch.Delete(_db.Collection("users").Document(uid).Collection("friendRequests").Document(fromUserId));
            await batch.CommitAsync();

            await CreateNotificationAsync(fromUserId, "friend_accepted", $"{myName} accepted your friend request");
            return Ok(new { message = "Friend request accepted." });
        }

        // POST /api/friends/requests/{fromUserId}/reject  { userId }
        [HttpPost("requests/{fromUserId}/reject")]
        public async Task<IActionResult> Reject(string fromUserId, [FromBody] FriendActionBody? body, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId, body?.UserId);
            if (uid == null) return NeedAuth();
            await _db.Collection("users").Document(uid).Collection("friendRequests").Document(fromUserId).DeleteAsync();
            return Ok(new { message = "Friend request rejected." });
        }

        // DELETE /api/friends/{friendId}?userId={uid}
        [HttpDelete("{friendId}")]
        public async Task<IActionResult> Remove(string friendId, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var batch = _db.StartBatch();
            batch.Delete(_db.Collection("users").Document(uid).Collection("friends").Document(friendId));
            batch.Delete(_db.Collection("users").Document(friendId).Collection("friends").Document(uid));
            await batch.CommitAsync();
            return Ok(new { message = "Friend removed." });
        }

        private async Task CreateNotificationAsync(string userId, string type, string message)
        {
            try
            {
                var id = Guid.NewGuid().ToString();
                await _db.Collection("users").Document(userId).Collection("notifications").Document(id)
                    .SetAsync(new Dictionary<string, object>
                    {
                        { "Id", id }, { "Type", type }, { "Message", message },
                        { "CreatedAt", DateTime.UtcNow.ToString("o") }, { "IsRead", false },
                        { "MeetupId", "" }, { "FriendRequestId", "" }, { "GroupId", "" }
                    });
            }
            catch { }
        }
    }
}
