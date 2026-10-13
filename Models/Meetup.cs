using Google.Cloud.Firestore;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class Meetup
    {
        [FirestoreProperty] public string Id { get; set; } = "";
        [FirestoreProperty] public string Title { get; set; } = "";
        [FirestoreProperty] public string Description { get; set; } = "";
        [FirestoreProperty] public string CreatorUserId { get; set; } = "";
        [FirestoreProperty] public string CreatorName { get; set; } = "";
        [FirestoreProperty] public string Location { get; set; } = "";
        [FirestoreProperty] public DateTime EventDateTime { get; set; }
        [FirestoreProperty] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [FirestoreProperty] public string Status { get; set; } = "confirmed"; // planning, confirmed, completed, cancelled
        [FirestoreProperty] public int ParticipantCount { get; set; } = 0;
    }
}