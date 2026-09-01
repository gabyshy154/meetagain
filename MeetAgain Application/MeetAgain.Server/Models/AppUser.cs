using Google.Cloud.Firestore;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class AppUser
    {
        [FirestoreProperty] public string Uid { get; set; } = "";
        [FirestoreProperty] public string Email { get; set; } = "";
        [FirestoreProperty] public string DisplayName { get; set; } = "";
        [FirestoreProperty] public string CreatedAt { get; set; } = "";

        public AppUser() { }

        public AppUser(string uid, string email, string displayName, string createdAt)
        {
            Uid = uid;
            Email = email;
            DisplayName = displayName;
            CreatedAt = createdAt;
        }
    }
}
