using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Api.Controllers;

[ApiController]
[Route("api/meetups")]
public class MeetupsController : ControllerBase
{
    private readonly MeetupService _svc;
    private readonly FirestoreService _fs;
    private readonly FirestoreDb _db;

    public MeetupsController(MeetupService svc, FirestoreService fs, FirestoreDb db)
    {
        _svc = svc;
        _fs = fs;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine() => Ok(await _svc.GetMyMeetupsAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMeetupRequest req)
        => Ok(await _svc.CreateMeetupAsync(req.Model, req.InvitedFriendIds));

    [HttpGet("{id}/detail")]
    public async Task<IActionResult> Detail(string id)
    {
        var detail = await _svc.GetMeetupDetailAsync(id);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    // The raw meetup record (used by the edit page). Only the creator or an invited person may read it.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRecord(string id)
    {
        var meetup = await _fs.GetMeetupByIdAsync(id);
        if (meetup == null) return NotFound();

        var uid = User.GetUid();
        if (meetup.CreatorUserId != uid)
        {
            var participant = await _db.Collection("meetups").Document(id)
                .Collection("participants").Document(uid).GetSnapshotAsync();
            if (!participant.Exists)
                return StatusCode(StatusCodes.Status403Forbidden);
        }
        return Ok(meetup);
    }

    [HttpPost("{id}/respond")]
    public async Task<IActionResult> Respond(string id, [FromBody] RespondRequest req)
        => Ok(await _svc.RespondToMeetupAsync(id, req.Status));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] Meetup meetup)
    {
        meetup.Id = id;
        return Ok(await _svc.UpdateMeetupAsync(meetup));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) => Ok(await _svc.DeleteMeetupAsync(id));
}
