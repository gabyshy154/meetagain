using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    public class GroupDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int MemberCount { get; set; }
        public bool IsOwner { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public List<GroupMemberDto> Members { get; set; } = new();
    }

    public class GroupMemberDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class CreateGroupModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> InitialMemberIds { get; set; } = new();
    }
}