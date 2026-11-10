using MeetAgain.Server.Models;

using Google.Cloud.Firestore;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class GroupInvite
    {
        [FirestoreProperty] public string UserId { get; set; } = "";
        [FirestoreProperty] public string SentBy { get; set; } = "";
        [FirestoreProperty] public string SentAt { get; set; } = "";
        [FirestoreProperty] public string Status { get; set; } = "pending";
    }
}