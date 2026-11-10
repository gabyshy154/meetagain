namespace MeetAgain.Server.Services
{

    // ── Models ──────────────────────────────────────────────────────────────────────────
    public class UserAvailability
    {
        public string UserId { get; set; } = "";
        public List<string> PreferredDays { get; set; } = new();
        public List<TimeSlot> AvailableTimeSlots { get; set; } = new();
        public List<DateTime> BlockedDates { get; set; } = new();
    }

    public class TimeSlot
    {
        public TimeOnly Start { get; set; }
        public TimeOnly End { get; set; }
    }

    public class SuggestedTimeSlot
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int AvailableParticipants { get; set; }
        public int TotalParticipants { get; set; }
        public double AvailabilityScore { get; set; }
        public int ConflictCount { get; set; }

        public string DisplayText =>
            $"{StartTime:ddd, MMM dd} at {StartTime:h:mm tt} – {EndTime:h:mm tt} " +
            $"({AvailableParticipants}/{TotalParticipants} available — {AvailabilityScore:F0}%)";
    }

}
