using MeetAgain.Server.Models;

namespace MeetAgain.Shared.Contracts;

public class IdResponse
{
    public string? Id { get; set; }
}

public class BestTimesRequest
{
    public List<string> ParticipantUserIds { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationMinutes { get; set; } = 120;
    public int MaxSuggestions { get; set; } = 5;
}

public class SendFriendRequestRequest
{
    public string Email { get; set; } = "";
}

public class AddMembersRequest
{
    public List<string> FriendIds { get; set; } = new();
    public bool SkipOwnerCheck { get; set; }   // ignored by the API on purpose
}

public class CreateMeetupRequest
{
    public CreateMeetupModel Model { get; set; } = new();
    public List<string>? InvitedFriendIds { get; set; }
}

public class RespondRequest
{
    public string Status { get; set; } = "";
}

public class CreateProfileRequest
{
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
}
