namespace MeetAgain.Server.Services
{

    // DTO for notifications with additional metadata
    public class NotificationDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public bool IsRead { get; set; } = false;
        public string? MeetupId { get; set; }
        public string? FriendRequestId { get; set; }
        public string? GroupId { get; set; }
    }

}
