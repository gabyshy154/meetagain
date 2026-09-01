using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class FriendRequest
    {
        [FirestoreProperty] public string Id { get; set; } = "";
        [FirestoreProperty] public string FromUserId { get; set; } = "";
        [FirestoreProperty] public string FromUserName { get; set; } = "";
        [FirestoreProperty] public string FromUserEmail { get; set; } = "";
        [FirestoreProperty] public string ToUserId { get; set; } = "";
        [FirestoreProperty] public string Status { get; set; } = "pending";
        [FirestoreProperty] public string SentAt { get; set; } = "";
    }
}