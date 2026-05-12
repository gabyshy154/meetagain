using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class Group
    {
        [FirestoreProperty] public string Id { get; set; } = "";
        [FirestoreProperty] public string OwnerId { get; set; } = "";
        [FirestoreProperty] public string Name { get; set; } = "";
        [FirestoreProperty] public string? Description { get; set; }
        [FirestoreProperty] public string CreatedAt { get; set; } = "";
    }

    [FirestoreData]
    public class GroupMember
    {
        [FirestoreProperty] public string UserId { get; set; } = "";
        [FirestoreProperty] public string AddedAt { get; set; } = "";
    }
}