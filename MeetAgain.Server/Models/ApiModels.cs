namespace MeetAgain.Server.Models
{
    public class SignupRequest
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public class LoginRequest
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class UpdateUserRequest
    {
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public class SendFriendRequestBody
    {
        public string UserId { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
    }

    public class FriendActionBody
    {
        public string UserId { get; set; } = "";
    }

    public class CreateGroupRequest
    {
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> InitialMemberIds { get; set; } = new();
    }

    public class AddGroupMembersRequest
    {
        public string UserId { get; set; } = "";
        public List<string> MemberIds { get; set; } = new();
    }

    public class CreateMeetupRequest
    {
        public string UserId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime EventDateTime { get; set; }
        public string Location { get; set; } = "";
        public List<string> InvitedFriendIds { get; set; } = new();
    }

    public class UpdateMeetupRequest
    {
        public string UserId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime EventDateTime { get; set; }
        public string Location { get; set; } = "";
        public string Status { get; set; } = "confirmed";
    }

    public class RsvpRequest
    {
        public string UserId { get; set; } = "";
        public string Status { get; set; } = "accepted";
    }

    public class TimeSlotDto
    {
        public string Start { get; set; } = "09:00";
        public string End { get; set; } = "22:00";
    }

    public class SaveAvailabilityRequest
    {
        public List<string> PreferredDays { get; set; } = new();
        public List<TimeSlotDto> AvailableTimeSlots { get; set; } = new();
        public List<DateTime> BlockedDates { get; set; } = new();
    }

    public class SuggestTimesRequest
    {
        public List<string> ParticipantUserIds { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int DurationMinutes { get; set; } = 120;
        public int MaxSuggestions { get; set; } = 5;
    }
}
