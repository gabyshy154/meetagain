using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class AvailabilityService
    {
        private readonly FirestoreDb _db;

        public AvailabilityService(FirestoreDb db)
        {
            _db = db;
        }

        // Get or create user availability document
        public async Task<UserAvailability> GetUserAvailabilityAsync(string userId)
        {
            var docRef = _db.Collection("users").Document(userId)
                           .Collection("settings").Document("availability");

            var snapshot = await docRef.GetSnapshotAsync();

            if (snapshot.Exists)
            {
                var data = snapshot.ToDictionary();
                return new UserAvailability
                {
                    UserId = userId,
                    PreferredDays = data.ContainsKey("PreferredDays") ? 
                        ((List<object>)data["PreferredDays"]).Select(d => d.ToString()!).ToList() : 
                        new List<string>(),
                    AvailableTimeSlots = data.ContainsKey("AvailableTimeSlots") ?
                        ((List<object>)data["AvailableTimeSlots"]).Select(t => ParseTimeSlot(t)).ToList() :
                        new List<TimeSlot>(),
                    BlockedDates = data.ContainsKey("BlockedDates") ?
                        ((List<object>)data["BlockedDates"]).Select(d => DateTime.Parse(d.ToString()!)).ToList() :
                        new List<DateTime>()
                };
            }

            // Return default availability (all days, 9 AM - 10 PM)
            return new UserAvailability
            {
                UserId = userId,
                PreferredDays = new List<string> { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
                AvailableTimeSlots = new List<TimeSlot>
                {
                    new TimeSlot { Start = new TimeOnly(9, 0), End = new TimeOnly(22, 0) }
                },
                BlockedDates = new List<DateTime>()
            };
        }

        // Save user availability preferences
        public async Task SaveUserAvailabilityAsync(UserAvailability availability)
        {
            var docRef = _db.Collection("users").Document(availability.UserId)
                           .Collection("settings").Document("availability");

            var data = new Dictionary<string, object>
            {
                { "PreferredDays", availability.PreferredDays },
                { "AvailableTimeSlots", availability.AvailableTimeSlots.Select(t => new Dictionary<string, object>
                    {
                        { "Start", t.Start.ToString("HH:mm") },
                        { "End", t.End.ToString("HH:mm") }
                    }).ToList() },
                { "BlockedDates", availability.BlockedDates.Select(d => d.ToString("yyyy-MM-dd")).ToList() }
            };

            await docRef.SetAsync(data, SetOptions.MergeAll);
        }

        // Find best meetup times for a group of users
        public async Task<List<SuggestedTimeSlot>> FindBestMeetupTimesAsync(
            List<string> participantUserIds,
            DateTime startDate,
            DateTime endDate,
            int durationMinutes = 120,
            int maxSuggestions = 5)
        {
            Console.WriteLine($"=== FindBestMeetupTimes ===");
            Console.WriteLine($"Participants: {participantUserIds.Count}");
            Console.WriteLine($"Date range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            
            // Get availability for all participants
            var availabilities = new List<UserAvailability>();
            foreach (var userId in participantUserIds)
            {
                var userAvail = await GetUserAvailabilityAsync(userId);
                availabilities.Add(userAvail);
                Console.WriteLine($"User {userId}: {userAvail.PreferredDays.Count} days, {userAvail.AvailableTimeSlots.Count} slots");
            }

            // Get existing meetups for all participants
            var existingMeetups = await GetExistingMeetupsAsync(participantUserIds, startDate, endDate);
            Console.WriteLine($"Found {existingMeetups.Count} existing meetups");

            var suggestions = new List<SuggestedTimeSlot>();

            // Iterate through each day in the range
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                var dayOfWeek = date.DayOfWeek.ToString();

                // Check if any users are blocked on this date
                if (availabilities.Any(a => a.BlockedDates.Any(bd => bd.Date == date.Date)))
                {
                    Console.WriteLine($"Skipping {date:yyyy-MM-dd} - blocked date");
                    continue;
                }

                // Find common time slots for this day
                var commonSlots = FindCommonTimeSlots(availabilities, dayOfWeek, date, existingMeetups, durationMinutes);
                if (commonSlots.Count > 0)
                {
                    Console.WriteLine($"{date:yyyy-MM-dd}: Found {commonSlots.Count} slots");
                }
                suggestions.AddRange(commonSlots);
            }

            Console.WriteLine($"Total suggestions before sorting: {suggestions.Count}");

            // Sort by score (availability percentage) and return top suggestions
            var topSuggestions = suggestions
                .OrderByDescending(s => s.AvailabilityScore)
                .ThenBy(s => s.StartTime)
                .Take(maxSuggestions)
                .ToList();
            
            Console.WriteLine($"Returning {topSuggestions.Count} top suggestions");
            foreach (var suggestion in topSuggestions)
            {
                Console.WriteLine($"  - {suggestion.StartTime:yyyy-MM-dd HH:mm} ({suggestion.AvailabilityScore:F0}%)");
            }

            return topSuggestions;
        }

        // Find common time slots for a specific day
        private List<SuggestedTimeSlot> FindCommonTimeSlots(
            List<UserAvailability> availabilities,
            string dayOfWeek,
            DateTime date,
            List<Meetup> existingMeetups,
            int durationMinutes)
        {
            var suggestions = new List<SuggestedTimeSlot>();

            // Filter users available on this day
            var availableUsers = availabilities
                .Where(a => a.PreferredDays.Contains(dayOfWeek))
                .ToList();

            // If no one has this day in preferences, assume everyone is available (default behavior)
            if (availableUsers.Count == 0)
            {
                Console.WriteLine($"No users have {dayOfWeek} in preferences, using default availability");
                availableUsers = availabilities; // Use all users with default slots
            }

            // Get all time slots from available users
            var allTimeSlots = availableUsers.SelectMany(a => a.AvailableTimeSlots).ToList();
            
            if (allTimeSlots.Count == 0)
            {
                Console.WriteLine($"No time slots defined for {dayOfWeek}, using defaults");
                // Use default time slots if none are defined
                allTimeSlots = new List<TimeSlot>
                {
                    new TimeSlot { Start = new TimeOnly(9, 0), End = new TimeOnly(22, 0) }
                };
            }

            // Get the broadest time range
            var earliestStart = allTimeSlots.Min(s => s.Start);
            var latestEnd = allTimeSlots.Max(s => s.End);

            // Check 30-minute intervals
            var currentTime = earliestStart;
            while (currentTime.AddMinutes(durationMinutes) <= latestEnd)
            {
                var endTime = currentTime.AddMinutes(durationMinutes);
                var startDateTime = date.Add(currentTime.ToTimeSpan());
                var endDateTime = date.Add(endTime.ToTimeSpan());

                // Count how many users are available during this slot
                int availableCount = 0;
                foreach (var userAvail in availabilities)
                {
                    if (IsUserAvailable(userAvail, currentTime, endTime, date, existingMeetups))
                    {
                        availableCount++;
                    }
                }

                // Calculate availability score
                double score = (double)availableCount / availabilities.Count * 100;

                // Only suggest if at least 50% are available (or at least 1 person if small group)
                var minAvailable = availabilities.Count > 2 ? availabilities.Count * 0.5 : 1;
                if (availableCount >= minAvailable)
                {
                    suggestions.Add(new SuggestedTimeSlot
                    {
                        StartTime = startDateTime,
                        EndTime = endDateTime,
                        AvailableParticipants = availableCount,
                        TotalParticipants = availabilities.Count,
                        AvailabilityScore = score,
                        ConflictCount = availabilities.Count - availableCount
                    });
                }

                currentTime = currentTime.AddMinutes(30); // Check every 30 minutes
            }

            return suggestions;
        }

        // Check if a user is available during a specific time slot
        private bool IsUserAvailable(
            UserAvailability userAvail,
            TimeOnly startTime,
            TimeOnly endTime,
            DateTime date,
            List<Meetup> existingMeetups)
        {
            // Check if time falls within user's available slots
            bool inAvailableSlot = userAvail.AvailableTimeSlots.Any(slot =>
                startTime >= slot.Start && endTime <= slot.End);

            if (!inAvailableSlot)
                return false;

            // Check for conflicts with existing meetups
            var startDateTime = date.Add(startTime.ToTimeSpan());
            var endDateTime = date.Add(endTime.ToTimeSpan());

            bool hasConflict = existingMeetups
                .Where(m => m.Status != "cancelled")
                .Any(m => m.EventDateTime < endDateTime && 
                         m.EventDateTime.AddHours(2) > startDateTime); // Assume 2-hour default duration

            return !hasConflict;
        }

        // Get existing meetups for participants in date range
        private async Task<List<Meetup>> GetExistingMeetupsAsync(
            List<string> participantUserIds,
            DateTime startDate,
            DateTime endDate)
        {
            var meetups = new List<Meetup>();

            // Convert to UTC for Firestore
            var startDateUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var endDateUtc = DateTime.SpecifyKind(endDate.AddDays(1), DateTimeKind.Utc); // Add 1 day to include end date

            var snapshot = await _db.Collection("meetups")
                                   .WhereGreaterThanOrEqualTo("EventDateTime", startDateUtc)
                                   .WhereLessThanOrEqualTo("EventDateTime", endDateUtc)
                                   .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var meetup = doc.ConvertTo<Meetup>();
                
                // Check if any participant is involved
                var participantsSnapshot = await doc.Reference
                    .Collection("participants")
                    .GetSnapshotAsync();

                var hasParticipant = participantsSnapshot.Documents
                    .Any(p => participantUserIds.Contains(p.Id));

                if (hasParticipant || participantUserIds.Contains(meetup.CreatorUserId))
                {
                    meetups.Add(meetup);
                }
            }

            return meetups;
        }

        // Helper to parse time slot from Firestore data
        private TimeSlot ParseTimeSlot(object data)
        {
            if (data is Dictionary<string, object> dict)
            {
                return new TimeSlot
                {
                    Start = TimeOnly.Parse(dict["Start"].ToString()!),
                    End = TimeOnly.Parse(dict["End"].ToString()!)
                };
            }
            return new TimeSlot();
        }
    }

    // Models for availability system
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
            $"{StartTime:ddd, MMM dd} at {StartTime:h:mm tt} - {EndTime:h:mm tt} " +
            $"({AvailableParticipants}/{TotalParticipants} available - {AvailabilityScore:F0}%)";
    }
}