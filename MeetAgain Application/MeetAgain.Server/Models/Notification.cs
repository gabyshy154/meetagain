using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class Notification
    {
        [FirestoreProperty] public string Id { get; set; } = "";
        [FirestoreProperty] public string Type { get; set; } = "";
        [FirestoreProperty] public string Message { get; set; } = "";
        [FirestoreProperty] public string CreatedAt { get; set; } = "";
        [FirestoreProperty] public bool IsRead { get; set; } = false;
        [FirestoreProperty] public string? MeetupId { get; set; }
        [FirestoreProperty] public string? GroupId { get; set; }
        [FirestoreProperty] public string? UserId { get; set; }
    }
}