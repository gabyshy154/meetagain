using MeetAgain.Server.Models;
using MeetAgain.Shared.Contracts;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Web-side client for groups (Stage C5). Firestore access, owner checks and
    /// group-invite notifications now run in MeetAgain.Api. This class keeps the
    /// same method names and signatures so the pages don't change.
    /// </summary>
    public class GroupService
    {
        private readonly ApiClient _api;

        public GroupService(ApiClient api)
        {
            _api = api;
        }

        // Returns the new group's id, or null if it could not be created.
        public async Task<string?> CreateGroupAsync(CreateGroupModel model)
        {
            var result = await _api.SendAsync<IdResponse>(HttpMethod.Post, "api/groups", model);
            return result?.Id;
        }

        public async Task<List<GroupDto>> GetMyGroupsAsync()
        {
            return await _api.GetAsync<List<GroupDto>>("api/groups")
                   ?? new List<GroupDto>();
        }

        public async Task<GroupDto?> GetGroupDetailAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return null;
            return await _api.GetAsync<GroupDto>($"api/groups/{Uri.EscapeDataString(groupId)}");
        }

        public async Task<GroupDto?> GetGroupByIdAsync(string groupId)
        {
            return await GetGroupDetailAsync(groupId);
        }

        // skipOwnerCheck is kept so old call sites still compile, but it is ignored:
        // the API always checks that the caller owns the group.
        public async Task<bool> AddMembersToGroupAsync(string groupId, List<string> friendIds,
            bool skipOwnerCheck = false)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Post,
                $"api/groups/{Uri.EscapeDataString(groupId)}/members",
                new AddMembersRequest { FriendIds = friendIds });
        }

        public async Task<bool> RemoveMemberAsync(string groupId, string memberId)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(memberId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Delete,
                $"api/groups/{Uri.EscapeDataString(groupId)}/members/{Uri.EscapeDataString(memberId)}");
        }

        public async Task<bool> DeleteGroupAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return false;
            return await _api.SendAsync<bool>(HttpMethod.Delete,
                $"api/groups/{Uri.EscapeDataString(groupId)}");
        }

        public async Task<List<string>> GetGroupMemberIdsAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId)) return new List<string>();
            return await _api.GetAsync<List<string>>(
                       $"api/groups/{Uri.EscapeDataString(groupId)}/member-ids")
                   ?? new List<string>();
        }
    }
}
