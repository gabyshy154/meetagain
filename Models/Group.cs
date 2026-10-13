using Google.Cloud.Firestore;

namespace MeetAgain.Server.Models
{
    /// <summary>
    /// Represents a friend group
    /// Stored in groups/{groupId}
    /// </summary>
    [FirestoreData]
    public class Group
    {
        [FirestoreProperty] public string Id { get; set; } = "";
        [FirestoreProperty] public string OwnerId { get; set; } = "";
        [FirestoreProperty] public string OwnerName { get; set; } = "";
        [FirestoreProperty] public string Name { get; set; } = "";
        [FirestoreProperty] public string Description { get; set; } = "";
        [FirestoreProperty] public int MemberCount { get; set; } = 0;
        [FirestoreProperty] public string CreatedAt { get; set; } = "";
    }

    /// <summary>
    /// Represents a member of a group
    /// Stored in groups/{groupId}/members/{userId}
    /// </summary>
    [FirestoreData]
    public class GroupMember
    {
        [FirestoreProperty] public string UserId { get; set; } = "";
        [FirestoreProperty] public string Name { get; set; } = "";
        [FirestoreProperty] public string Email { get; set; } = "";
        [FirestoreProperty] public string AddedAt { get; set; } = "";
        [FirestoreProperty] public string AddedBy { get; set; } = ""; // UserId who added this member
    }

    /// <summary>
    /// DTO for displaying groups with member info
    /// </summary>
    public class GroupDto
    {
        public string Id { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int MemberCount { get; set; } = 0;
        public string CreatedAt { get; set; } = "";
        public bool IsOwner { get; set; } = false;
        public List<GroupMember> Members { get; set; } = new();
    }

    /// <summary>
    /// Model for creating a new group
    /// </summary>
    public class CreateGroupModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> InitialMemberIds { get; set; } = new();
    }
}