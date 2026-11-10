using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationService _svc;

    public NotificationsController(NotificationService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetMine() => Ok(await _svc.GetMyNotificationsAsync());

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() => Ok(await _svc.GetUnreadCountAsync());

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        await _svc.MarkAsReadAsync(id);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _svc.MarkAllAsReadAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _svc.DeleteNotificationAsync(id);
        return NoContent();
    }
}
