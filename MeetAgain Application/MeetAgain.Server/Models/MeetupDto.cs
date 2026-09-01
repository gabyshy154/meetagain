using MeetAgain.Server.Models;

namespace MeetAgain.Server.Models
{
    public class MeetupDto
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Location { get; set; }
        public DateTime EventDateTime { get; set; }
        public string? RsvpStatus { get; set; }
        public bool IsCreator { get; set; }
        public string MyRSVPStatus { get; set; } = "";
        public int ParticipantCount { get; set; }
        public string CreatorName { get; set; } = "";
    }
}