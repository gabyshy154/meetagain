using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly FirestoreDb _db;

        public GroupsController(FirestoreDb db)
        {
            _db = db;
        }

        private Task<string?> CallerUidAsync(string? q = null, string? b = null)
            => ApiUserContext.RequireUserIdAsync(HttpContext, q, b);

        private IActionResult NeedAuth() =>
            Unauthorized(new { error = "Missing user identity. Send Authorization: Bearer <FirebaseIdToken>, X-User-Id header, or userId in query/body." });

        // GET /api/groups?userId={uid}
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var all = await _db.Collection("groups").GetSnapshotAsync();
            var result = new List<GroupDto>();
            foreach (var g in all.Documents)
            {
                var group = g.ConvertTo<Group>();
                if (group.OwnerId == uid)
                {
                    result.Add(new GroupDto { Id = group.Id, OwnerId = group.OwnerId, OwnerName = group.OwnerName, Name = group.Name, Description = group.Description, MemberCount = group.MemberCount, CreatedAt = group.CreatedAt, IsOwner = true });
                    continue;
                }
                if ((await _db.Collection("groups").Document(group.Id).Collection("members").Document(uid).GetSnapshotAsync()).Exists)
                    result.Add(new GroupDto { Id = group.Id, OwnerId = group.OwnerId, OwnerName = group.OwnerName, Name = group.Name, Description = group.Description, MemberCount = group.MemberCount, CreatedAt = group.CreatedAt, IsOwner = false });
            }
            return Ok(result.OrderBy(g => g.Name).ToList());
        }

        // GET /api/groups/{id}?userId={uid}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetail(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var doc = await _db.Collection("groups").Document(id).GetSnapshotAsync();
            if (!doc.Exists) return NotFound(new { error = "Group not found." });
            var group = doc.ConvertTo<Group>();

            var membersSnap = await _db.Collection("groups").Document(id).Collection("members").GetSnapshotAsync();
            var members = membersSnap.Documents.Select(d => d.ConvertTo<GroupMember>()).OrderBy(m => m.Name).ToList();

            return Ok(new GroupDto
            {
                Id = group.Id, OwnerId = group.OwnerId, OwnerName = group.OwnerName,
                Name = group.Name, Description = group.Description, MemberCount = group.MemberCount,
                CreatedAt = group.CreatedAt, IsOwner = group.OwnerId == uid, Members = members
            });
        }

        // POST /api/groups  { userId, name, description, initialMemberIds[] }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateGroupRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name is required." });

            var userDoc = await _db.Collection("users").Document(uid).GetSnapshotAsync();
            if (!userDoc.Exists) return NotFound(new { error = "Caller user document not found." });
            var ownerName = userDoc.ToDictionary().TryGetValue("DisplayName", out var n) ? n?.ToString() ?? "" : "";

            var groupId = Guid.NewGuid().ToString("N");
            var group = new Group
            {
                Id = groupId, OwnerId = uid, OwnerName = ownerName,
                Name = req.Name, Description = req.Description,
                MemberCount = req.InitialMemberIds?.Count ?? 0,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };
            await _db.Collection("groups").Document(groupId).SetAsync(group);

            if (req.InitialMemberIds is { Count: > 0 })
                await AddMembersInternalAsync(groupId, uid, req.InitialMemberIds);

            return CreatedAtAction(nameof(GetDetail), new { id = groupId }, new { id = groupId, group });
        }

        // POST /api/groups/{id}/members  { userId(owner), memberIds[] }
        [HttpPost("{id}/members")]
        public async Task<IActionResult> AddMembers(string id, [FromBody] AddGroupMembersRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            var ok = await AddMembersInternalAsync(id, uid, req.MemberIds ?? new());
            if (!ok) return Forbid();
            return Ok(new { message = "Members added." });
        }

        // DELETE /api/groups/{id}/members/{memberId}?userId={ownerUid}
        [HttpDelete("{id}/members/{memberId}")]
        public async Task<IActionResult> RemoveMember(string id, string memberId, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var doc = await _db.Collection("groups").Document(id).GetSnapshotAsync();
            if (!doc.Exists) return NotFound(new { error = "Group not found." });
            var group = doc.ConvertTo<Group>();
            if (group.OwnerId != uid) return Forbid();

            var batch = _db.StartBatch();
            batch.Delete(_db.Collection("groups").Document(id).Collection("members").Document(memberId));
            batch.Update(_db.Collection("groups").Document(id), new Dictionary<string, object> { { "MemberCount", Math.Max(0, group.MemberCount - 1) } });
            await batch.CommitAsync();
            return Ok(new { message = "Member removed." });
        }

        // DELETE /api/groups/{id}?userId={ownerUid}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var doc = await _db.Collection("groups").Document(id).GetSnapshotAsync();
            if (!doc.Exists) return NotFound(new { error = "Group not found." });
            if (doc.ConvertTo<Group>().OwnerId != uid) return Forbid();

            var members = await _db.Collection("groups").Document(id).Collection("members").GetSnapshotAsync();
            var batch = _db.StartBatch();
            foreach (var m in members.Documents) batch.Delete(m.Reference);
            batch.Delete(_db.Collection("groups").Document(id));
            await batch.CommitAsync();
            return Ok(new { message = "Group deleted." });
        }

        private async Task<bool> AddMembersInternalAsync(string groupId, string ownerUid, List<string> friendIds)
        {
            try
            {
                var doc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!doc.Exists) return false;
                var group = doc.ConvertTo<Group>();
                if (group.OwnerId != ownerUid) return false;

                var batch = _db.StartBatch();
                var added = 0;
                foreach (var fid in friendIds.Distinct())
                {
                    if ((await _db.Collection("groups").Document(groupId).Collection("members").Document(fid).GetSnapshotAsync()).Exists)
                        continue;
                    var friend = await _db.Collection("users").Document(fid).GetSnapshotAsync();
                    if (!friend.Exists) continue;
                    var fd = friend.ToDictionary();
                    batch.Set(_db.Collection("groups").Document(groupId).Collection("members").Document(fid),
                        new GroupMember
                        {
                            UserId = fid,
                            Name = fd.TryGetValue("DisplayName", out var n) ? n?.ToString() ?? "" : "",
                            Email = fd.TryGetValue("Email", out var e) ? e?.ToString() ?? "" : "",
                            AddedAt = DateTime.UtcNow.ToString("o"),
                            AddedBy = ownerUid
                        });
                    added++;
                }
                if (added > 0)
                    batch.Update(_db.Collection("groups").Document(groupId), new Dictionary<string, object> { { "MemberCount", group.MemberCount + added } });
                await batch.CommitAsync();
                return true;
            }
            catch { return false; }
        }
    }
}
