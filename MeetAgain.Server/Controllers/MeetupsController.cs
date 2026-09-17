using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Server.Controllers
{
    [ApiController]
    [Route("api/meetups")]
    public class MeetupsController : ControllerBase
    {
        private readonly FirestoreDb _db;

        public MeetupsController(FirestoreDb db)
        {
            _db = db;
        }

        private Task<string?> CallerUidAsync(string? q = null, string? b = null)
            => ApiUserContext.RequireUserIdAsync(HttpContext, q, b);

        private IActionResult NeedAuth() =>
            Unauthorized(new { error = "Missing user identity. Send Authorization: Bearer <FirebaseIdToken>, X-User-Id header, or userId in query/body." });

        // GET /api/meetups?userId={uid}
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var all = await _db.Collection("meetups").GetSnapshotAsync();
            var result = new List<MeetupDto>();
            foreach (var doc in all.Documents)
            {
                var meetup = doc.ConvertTo<Meetup>();
                var part = await _db.Collection("meetups").Document(doc.Id).Collection("participants").Document(uid).GetSnapshotAsync();
                if (!part.Exists) continue;
                var p = part.ConvertTo<MeetupParticipant>();
                result.Add(new MeetupDto
                {
                    Id = meetup.Id, Title = meetup.Title, Description = meetup.Description,
                    CreatorUserId = meetup.CreatorUserId, CreatorName = meetup.CreatorName,
                    Location = meetup.Location, EventDateTime = meetup.EventDateTime,
                    CreatedAt = meetup.CreatedAt, Status = meetup.Status,
                    ParticipantCount = meetup.ParticipantCount,
                    IsCreator = meetup.CreatorUserId == uid, MyRSVPStatus = p.Status
                });
            }
            return Ok(result.OrderBy(m => m.EventDateTime).ToList());
        }

        // GET /api/meetups/{id}?userId={uid}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetail(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var doc = await _db.Collection("meetups").Document(id).GetSnapshotAsync();
            if (!doc.Exists) return NotFound(new { error = "Meetup not found." });
            var meetup = doc.ConvertTo<Meetup>();

            var parts = await _db.Collection("meetups").Document(id).Collection("participants").GetSnapshotAsync();
            var participants = parts.Documents.Select(d => d.ConvertTo<MeetupParticipant>()).OrderBy(p => p.Name).ToList();
            return Ok(new MeetupDetailDto
            {
                Meetup = meetup,
                Participants = participants,
                IsCreator = meetup.CreatorUserId == uid,
                MyRSVPStatus = participants.FirstOrDefault(p => p.UserId == uid)?.Status ?? ""
            });
        }

        // POST /api/meetups  { userId, title, description, eventDateTime, location, invitedFriendIds[] }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateMeetupRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { error = "title is required." });
            if (req.EventDateTime == default) return BadRequest(new { error = "eventDateTime is required (ISO 8601)." });

            var userDoc = await _db.Collection("users").Document(uid).GetSnapshotAsync();
            if (!userDoc.Exists) return NotFound(new { error = "Caller user document not found." });
            var ud = userDoc.ToDictionary();
            var creatorName = ud.TryGetValue("DisplayName", out var n) ? n?.ToString() ?? "" : "";
            var creatorEmail = ud.TryGetValue("Email", out var e) ? e?.ToString() ?? "" : "";

            var meetupId = Guid.NewGuid().ToString("N");
            var meetup = new Meetup
            {
                Id = meetupId, Title = req.Title, Description = req.Description,
                CreatorUserId = uid, CreatorName = creatorName, Location = req.Location ?? "",
                EventDateTime = DateTime.SpecifyKind(req.EventDateTime, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow, Status = "confirmed",
                ParticipantCount = (req.InvitedFriendIds?.Count ?? 0) + 1
            };
            await _db.Collection("meetups").Document(meetupId).SetAsync(meetup);
            await _db.Collection("meetups").Document(meetupId).Collection("participants").Document(uid)
                .SetAsync(new MeetupParticipant
                {
                    UserId = uid, Name = creatorName, Email = creatorEmail,
                    Status = "accepted", InvitedAt = DateTime.UtcNow.ToString("o"), RespondedAt = DateTime.UtcNow.ToString("o")
                });

            if (req.InvitedFriendIds is { Count: > 0 })
            {
                var batch = _db.StartBatch();
                foreach (var fid in req.InvitedFriendIds.Distinct())
                {
                    if ((await _db.Collection("meetups").Document(meetupId).Collection("participants").Document(fid).GetSnapshotAsync()).Exists)
                        continue;
                    var fdoc = await _db.Collection("users").Document(fid).GetSnapshotAsync();
                    if (!fdoc.Exists) continue;
                    var fd = fdoc.ToDictionary();
                    batch.Set(_db.Collection("meetups").Document(meetupId).Collection("participants").Document(fid),
                        new MeetupParticipant
                        {
                            UserId = fid,
                            Name = fd.TryGetValue("DisplayName", out var fn) ? fn?.ToString() ?? "" : "",
                            Email = fd.TryGetValue("Email", out var fe) ? fe?.ToString() ?? "" : "",
                            Status = "invited", InvitedAt = DateTime.UtcNow.ToString("o"), RespondedAt = ""
                        });
                    var notifId = Guid.NewGuid().ToString();
                    batch.Set(_db.Collection("users").Document(fid).Collection("notifications").Document(notifId),
                        new Dictionary<string, object>
                        {
                            { "Id", notifId }, { "Type", "meetup_invite" },
                            { "Message", $"{creatorName} invited you to '{req.Title}'" },
                            { "MeetupId", meetupId }, { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false }, { "FriendRequestId", "" }, { "GroupId", "" }
                        });
                }
                await batch.CommitAsync();
            }

            return CreatedAtAction(nameof(GetDetail), new { id = meetupId }, meetup);
        }

        // PUT /api/meetups/{id}  { userId(creator), title, description, eventDateTime, location, status }
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateMeetupRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();

            var doc = await _db.Collection("meetups").Document(id).GetSnapshotAsync();
            if (!doc.Exists) return NotFound(new { error = "Meetup not found." });
            var meetup = doc.ConvertTo<Meetup>();
            if (meetup.CreatorUserId != uid) return Forbid();

            if (!string.IsNullOrWhiteSpace(req.Title)) meetup.Title = req.Title;
            if (req.Description != null) meetup.Description = req.Description;
            if (req.EventDateTime != default) meetup.EventDateTime = DateTime.SpecifyKind(req.EventDateTime, DateTimeKind.Utc);
            if (req.Location != null) meetup.Location = req.Location;
            if (!string.IsNullOrWhiteSpace(req.Status)) meetup.Status = req.Status;

            await _db.Collection("meetups").Document(id).SetAsync(meetup);

            var parts = await _db.Collection("meetups").Document(id).Collection("participants").GetSnapshotAsync();
            var batch = _db.StartBatch();
            foreach (var p in parts.Documents)
            {
                var participant = p.ConvertTo<MeetupParticipant>();
                if (participant.UserId != uid && (participant.Status == "accepted" || participant.Status == "invited"))
                {
                    var notifId = Guid.NewGuid().ToString();
                    batch.Set(_db.Collection("users").Document(participant.UserId).Collection("notifications").Document(notifId),
                        new Dictionary<string, object>
                        {
                            { "Id", notifId }, { "Type", "meetup_update" },
                            { "Message", $"'{meetup.Title}' has been updated" },
                            { "MeetupId", id }, { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false }, { "FriendRequestId", "" }, { "GroupId", "" }
                        });
                }
            }
            await batch.CommitAsync();
            return Ok(meetup);
        }

        // DELETE /api/meetups/{id}?userId={creatorUid}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromQuery] string? userId)
        {
            var uid = await CallerUidAsync(userId);
            if (uid == null) return NeedAuth();

            var doc = await _db.Collection("meetups").Document(id).GetSnapshotAsync();
            if (!doc.Exists) return NotFound(new { error = "Meetup not found." });
            var meetup = doc.ConvertTo<Meetup>();
            if (meetup.CreatorUserId != uid) return Forbid();

            var parts = await _db.Collection("meetups").Document(id).Collection("participants").GetSnapshotAsync();
            var batch = _db.StartBatch();
            foreach (var p in parts.Documents)
            {
                var participant = p.ConvertTo<MeetupParticipant>();
                if (participant.UserId != uid)
                {
                    var notifId = Guid.NewGuid().ToString();
                    batch.Set(_db.Collection("users").Document(participant.UserId).Collection("notifications").Document(notifId),
                        new Dictionary<string, object>
                        {
                            { "Id", notifId }, { "Type", "meetup_update" },
                            { "Message", $"'{meetup.Title}' has been cancelled" },
                            { "MeetupId", id }, { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false }, { "FriendRequestId", "" }, { "GroupId", "" }
                        });
                }
                batch.Delete(p.Reference);
            }
            batch.Delete(_db.Collection("meetups").Document(id));
            await batch.CommitAsync();
            return Ok(new { message = "Meetup deleted." });
        }

        // POST /api/meetups/{id}/rsvp  { userId, status: accepted|declined|maybe }
        [HttpPost("{id}/rsvp")]
        public async Task<IActionResult> Rsvp(string id, [FromBody] RsvpRequest req)
        {
            var uid = await CallerUidAsync(null, req.UserId);
            if (uid == null) return NeedAuth();
            var allowed = new[] { "accepted", "declined", "maybe", "invited" };
            if (!allowed.Contains(req.Status)) return BadRequest(new { error = "status must be one of: accepted, declined, maybe." });

            var pref = _db.Collection("meetups").Document(id).Collection("participants").Document(uid);
            var pdoc = await pref.GetSnapshotAsync();
            if (!pdoc.Exists) return NotFound(new { error = "You are not a participant of this meetup." });

            await pref.UpdateAsync(new Dictionary<string, object>
            {
                { "Status", req.Status }, { "RespondedAt", DateTime.UtcNow.ToString("o") }
            });

            var mdoc = await _db.Collection("meetups").Document(id).GetSnapshotAsync();
            if (mdoc.Exists)
            {
                var meetup = mdoc.ConvertTo<Meetup>();
                var userName = pdoc.ToDictionary().TryGetValue("Name", out var nm) ? nm?.ToString() ?? "" : "";
                if (meetup.CreatorUserId != uid)
                {
                    var statusText = req.Status switch { "accepted" => "accepted", "declined" => "declined", "maybe" => "responded 'maybe' to", _ => "responded to" };
                    var notifId = Guid.NewGuid().ToString();
                    await _db.Collection("users").Document(meetup.CreatorUserId).Collection("notifications").Document(notifId)
                        .SetAsync(new Dictionary<string, object>
                        {
                            { "Id", notifId }, { "Type", "rsvp_change" },
                            { "Message", $"{userName} {statusText} your meetup '{meetup.Title}'" },
                            { "MeetupId", id }, { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false }, { "FriendRequestId", "" }, { "GroupId", "" }
                        });
                }
            }
            return Ok(new { message = $"RSVP set to {req.Status}." });
        }
    }
}
