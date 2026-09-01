using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class MeetupParticipant
    {
        [FirestoreProperty] public string Id { get; set; } = Guid.NewGuid().ToString();
        [FirestoreProperty] public string MeetupId { get; set; } = string.Empty;
        [FirestoreProperty] public string UserId { get; set; } = string.Empty;
        [FirestoreProperty] public string UserName { get; set; } = string.Empty;
        [FirestoreProperty] public string Status { get; set; } = "pending";
    }
}