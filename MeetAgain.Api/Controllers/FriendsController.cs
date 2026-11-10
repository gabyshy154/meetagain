using MeetAgain.Server.Services;
using MeetAgain.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Api.Controllers;

[ApiController]
[Route("api/friends")]
public class FriendsController : ControllerBase
{
    private readonly FriendService _svc;

    public FriendsController(FriendService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetFriends() => Ok(await _svc.GetFriendsAsync());

    [HttpDelete("{friendId}")]
    public async Task<IActionResult> Remove(string friendId) => Ok(await _svc.RemoveFriendAsync(friendId));

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests() => Ok(await _svc.GetFriendRequestsAsync());

    [HttpPost("requests")]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestRequest req)
        => Ok(await _svc.SendFriendRequestAsync(req.Email));

    [HttpPost("requests/{fromUserId}/accept")]
    public async Task<IActionResult> Accept(string fromUserId) => Ok(await _svc.AcceptFriendRequestAsync(fromUserId));

    [HttpPost("requests/{fromUserId}/reject")]
    public async Task<IActionResult> Reject(string fromUserId) => Ok(await _svc.RejectFriendRequestAsync(fromUserId));

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string email)
    {
        var user = await _svc.SearchUserByEmailAsync(email);
        if (user == null) return NotFound();
        return Ok(user);
    }
}
