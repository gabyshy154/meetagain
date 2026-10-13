namespace MeetAgain.Server.Models
{
    /// <summary>
    /// DTO for displaying meetup in lists (dashboard, meetups page)
    /// Includes participant info and user-specific data
    /// </summary>
    public class MeetupDto
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string CreatorUserId { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public string Location { get; set; } = "";
        public DateTime EventDateTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "confirmed";
        public int ParticipantCount { get; set; } = 0;
        
        // User-specific properties
        public bool IsCreator { get; set; } = false;
        public string MyRSVPStatus { get; set; } = ""; // For invited users: invited, accepted, declined, maybe
    }

    /// <summary>
    /// DTO for displaying full meetup details with participant list
    /// Used on meetup detail page
    /// </summary>
    public class MeetupDetailDto
    {
        public Meetup Meetup { get; set; } = new();
        public List<MeetupParticipant> Participants { get; set; } = new();
        public bool IsCreator { get; set; } = false;
        public string MyRSVPStatus { get; set; } = "";
    }
}