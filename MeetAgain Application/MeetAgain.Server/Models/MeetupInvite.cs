using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class MeetupInvite
    {
        [FirestoreProperty] public string UserId { get; set; } = "";
        [FirestoreProperty] public string Status { get; set; } = "pending";
        [FirestoreProperty] public string SentAt { get; set; } = "";
    }
}