using Google.Cloud.Firestore;

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
        
        // Optional metadata fields
        [FirestoreProperty] public string MeetupId { get; set; } = "";
        [FirestoreProperty] public string FriendRequestId { get; set; } = "";
        [FirestoreProperty] public string GroupId { get; set; } = "";
    }
}