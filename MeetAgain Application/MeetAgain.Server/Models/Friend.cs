using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class Friend
    {
        [FirestoreProperty] public string Id { get; set; } = "";
        [FirestoreProperty] public string UserId { get; set; } = "";
        [FirestoreProperty] public string FriendUserId { get; set; } = "";
        [FirestoreProperty] public string Name { get; set; } = "";
        [FirestoreProperty] public string Email { get; set; } = "";
        [FirestoreProperty] public string AddedAt { get; set; } = "";
    }
}