using MeetAgain.Server.Models;
using MeetAgain.Server.Services;
using MeetAgain.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace MeetAgain.Api.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController : ControllerBase
{
    private readonly GroupService _svc;

    public GroupsController(GroupService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetMine() => Ok(await _svc.GetMyGroupsAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGroupModel model)
    {
        var id = await _svc.CreateGroupAsync(model);
        if (id == null) return BadRequest();
        return Ok(new IdResponse { Id = id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(string id)
    {
        var group = await _svc.GetGroupDetailAsync(id);
        if (group == null) return NotFound();
        return Ok(group);
    }

    [HttpGet("{id}/member-ids")]
    public async Task<IActionResult> MemberIds(string id) => Ok(await _svc.GetGroupMemberIdsAsync(id));

    // SkipOwnerCheck from the client is ignored: only the owner may add members
    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddMembers(string id, [FromBody] AddMembersRequest req)
        => Ok(await _svc.AddMembersToGroupAsync(id, req.FriendIds));

    [HttpDelete("{id}/members/{memberId}")]
    public async Task<IActionResult> RemoveMember(string id, string memberId)
        => Ok(await _svc.RemoveMemberAsync(id, memberId));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) => Ok(await _svc.DeleteGroupAsync(id));
}
