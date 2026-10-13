using Google.Cloud.Firestore;

namespace MeetAgain.Server.Models
{
    /// <summary>
    /// Represents a participant/invitee for a meetup
    /// Stored in meetups/{meetupId}/participants/{userId}
    /// </summary>
    [FirestoreData]
    public class MeetupParticipant
    {
        [FirestoreProperty] public string UserId { get; set; } = "";
        [FirestoreProperty] public string Name { get; set; } = "";
        [FirestoreProperty] public string Email { get; set; } = "";
        [FirestoreProperty] public string Status { get; set; } = "invited"; // invited, accepted, declined, maybe
        [FirestoreProperty] public string InvitedAt { get; set; } = "";
        [FirestoreProperty] public string RespondedAt { get; set; } = "";
    }
}