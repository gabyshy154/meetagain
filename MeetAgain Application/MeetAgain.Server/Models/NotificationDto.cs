using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    public class NotificationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? MeetupId { get; set; }
        public string? GroupId { get; set; }
        public string? UserId { get; set; }
    }
}