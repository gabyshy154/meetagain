using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly FirestoreService _fs;

    public UsersController(FirestoreService fs) => _fs = fs;

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var user = await _fs.GetUserAsync(User.GetUid());
        if (user == null) return NotFound();
        return Ok(user);
    }

    // Creates (or updates) the caller's own profile. Used right after sign-up.
    [HttpPost("me")]
    public async Task<IActionResult> SaveMe([FromBody] CreateProfileRequest req)
    {
        // NEW – Stage D: cap the display name length
        if (req.DisplayName?.Trim().Length > 100)
            return BadRequest("Display name too long.");

        var uid = User.GetUid();

        // NEW – Stage D: the email comes ONLY from the verified token, never from the request body
        var email = User.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Token has no email.");

        var existing = await _fs.GetUserAsync(uid);

        var profile = new AppUser
        {
            Uid = uid,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName)
                ? (existing?.DisplayName ?? email)
                : req.DisplayName.Trim(),
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow.ToString("o")
        };

        await _fs.CreateOrUpdateUserAsync(profile);
        return Ok(profile);
    }
}
