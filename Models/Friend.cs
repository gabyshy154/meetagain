using MeetAgain.Server.Models;
using Google.Cloud.Firestore;

namespace MeetAgain.Server.Models
{
    [FirestoreData]
    public class Friend
    {
        [FirestoreProperty] public string Id { get; set; } = "";     
        [FirestoreProperty] public string Name { get; set; } = "";
        [FirestoreProperty] public string Email { get; set; } = "";
        [FirestoreProperty] public string AddedAt { get; set; } = "";
    }
}
