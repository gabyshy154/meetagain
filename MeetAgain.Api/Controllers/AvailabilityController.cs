using MeetAgain.Server.Services;
using MeetAgain.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Api.Controllers;

[ApiController]
[Route("api/availability")]
public class AvailabilityController : ControllerBase
{
    private readonly AvailabilityService _svc;

    public AvailabilityController(AvailabilityService svc) => _svc = svc;

    // You can only read your own settings
    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId)
    {
        if (userId != User.GetUid())
            return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await _svc.GetUserAvailabilityAsync(userId));
    }

    // You can only save your own settings (the id in the body is ignored)
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] UserAvailability availability)
    {
        availability.UserId = User.GetUid();
        await _svc.SaveUserAvailabilityAsync(availability);
        return NoContent();
    }

    [HttpPost("best-times")]
    public async Task<IActionResult> BestTimes([FromBody] BestTimesRequest req)
    {
        var slots = await _svc.FindBestMeetupTimesAsync(
            req.ParticipantUserIds, req.StartDate, req.EndDate, req.DurationMinutes, req.MaxSuggestions);
        return Ok(slots);
    }
}
