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

                // ── Preferred Days ────────────────────────────────────────────────────
                var preferredDays = new List<string>();
                if (data.ContainsKey("PreferredDays") && data["PreferredDays"] is List<object> daysList)
                    preferredDays = daysList.Select(d => d.ToString()!).ToList();

                // ── Time Slots ────────────────────────────────────────────────────────
                var timeSlots = new List<TimeSlot>();
                if (data.ContainsKey("AvailableTimeSlots") && data["AvailableTimeSlots"] is List<object> slotsList)
                    timeSlots = slotsList.Select(t => ParseTimeSlot(t)).Where(t => t != null).ToList()!;

                // ── Blocked Dates ─────────────────────────────────────────────────────
                // Stored as "yyyy-MM-dd" strings — parse them directly to avoid UTC drift
                var blockedDates = new List<DateTime>();
                if (data.ContainsKey("BlockedDates") && data["BlockedDates"] is List<object> datesList)
                {
                    foreach (var d in datesList)
                    {
                        var str = d?.ToString() ?? "";
                        // Handle both plain date strings and full ISO timestamps
                        if (DateTime.TryParse(str, out var parsed))
                            blockedDates.Add(parsed.Date); // always strip time
                    }
                }

                // If a user has saved data but left PreferredDays empty, treat all days as preferred
                if (preferredDays.Count == 0)
                    preferredDays = new List<string> { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

                // If a user has saved data but left time slots empty, use the default window
                if (timeSlots.Count == 0)
                    timeSlots = new List<TimeSlot> { new TimeSlot { Start = new TimeOnly(9, 0), End = new TimeOnly(22, 0) } };

                return new UserAvailability
                {
                    UserId        = userId,
                    PreferredDays       = preferredDays,
                    AvailableTimeSlots  = timeSlots,
                    BlockedDates        = blockedDates
                };
            }

            // ── Default availability (new user, never saved settings) ─────────────────
            return new UserAvailability
            {
                UserId        = userId,
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
                {
                    "AvailableTimeSlots",
                    availability.AvailableTimeSlots.Select(t => new Dictionary<string, object>
                    {
                        { "Start", t.Start.ToString("HH:mm") },
                        { "End",   t.End.ToString("HH:mm")   }
                    }).ToList<object>()
                },
                // Always store as plain "yyyy-MM-dd" — no time, no timezone ambiguity
                { "BlockedDates", availability.BlockedDates.Select(d => d.Date.ToString("yyyy-MM-dd")).ToList<object>() }
            };

            // Use SetAsync (overwrite) so deleted days/slots are properly removed
            await docRef.SetAsync(data);
        }

        // ── Public entry point ─────────────────────────────────────────────────────────
        // NOTE: participantUserIds should already include the meetup creator's UID
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

            // Deduplicate just in case
            participantUserIds = participantUserIds.Distinct().ToList();

            // Fetch availability for every participant
            var availabilities = new List<UserAvailability>();
            foreach (var userId in participantUserIds)
            {
                var userAvail = await GetUserAvailabilityAsync(userId);
                availabilities.Add(userAvail);
                Console.WriteLine($"  User {userId}: days=[{string.Join(",", userAvail.PreferredDays)}] " +
                                  $"slots={userAvail.AvailableTimeSlots.Count} " +
                                  $"blocked={userAvail.BlockedDates.Count}");
            }

            // Fetch existing meetups that might conflict
            var existingMeetups = await GetExistingMeetupsAsync(participantUserIds, startDate, endDate);
            Console.WriteLine($"Existing meetups found: {existingMeetups.Count}");

            var suggestions = new List<SuggestedTimeSlot>();

            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                // Map DateTime.DayOfWeek to the string names we store
                var dayOfWeek = MapDayOfWeek(date.DayOfWeek);

                // ── BLOCKED DATE CHECK ────────────────────────────────────────────────
                // Skip the whole day if ANY participant has it blocked
                if (availabilities.Any(a => a.BlockedDates.Any(bd => bd.Date == date.Date)))
                {
                    Console.WriteLine($"Skipping {date:yyyy-MM-dd} ({dayOfWeek}) — blocked by at least one participant");
                    continue;
                }

                var daySlots = FindSlotsForDay(availabilities, dayOfWeek, date, existingMeetups, durationMinutes);
                if (daySlots.Count > 0)
                    Console.WriteLine($"{date:yyyy-MM-dd} ({dayOfWeek}): {daySlots.Count} candidate slots");

                suggestions.AddRange(daySlots);
            }

            Console.WriteLine($"Total candidates: {suggestions.Count}");

            var top = suggestions
                .OrderByDescending(s => s.AvailabilityScore)
                .ThenBy(s => s.StartTime)
                .Take(maxSuggestions)
                .ToList();

            Console.WriteLine($"Returning {top.Count} suggestions:");
            foreach (var s in top)
                Console.WriteLine($"  {s.StartTime:yyyy-MM-dd HH:mm}  score={s.AvailabilityScore:F0}%  " +
                                  $"({s.AvailableParticipants}/{s.TotalParticipants})");

            return top;
        }

        // ── Core day-level logic ───────────────────────────────────────────────────────
        private List<SuggestedTimeSlot> FindSlotsForDay(
            List<UserAvailability> availabilities,
            string dayOfWeek,
            DateTime date,
            List<Meetup> existingMeetups,
            int durationMinutes)
        {
            var suggestions = new List<SuggestedTimeSlot>();

            // ── PREFERRED DAYS ────────────────────────────────────────────────────────
            // Only consider users who have this day in their preferred days list.
            // Users who haven't touched settings get all days by default (see GetUserAvailabilityAsync).
            // If a user explicitly removed a day, respect that — they are NOT available.
            var usersAvailableToday = availabilities
                .Where(a => a.PreferredDays.Contains(dayOfWeek))
                .ToList();

            if (usersAvailableToday.Count == 0)
            {
                Console.WriteLine($"  {dayOfWeek}: no participants prefer this day — skipping");
                return suggestions; // All participants are unavailable → no slots
            }

            // ── TIME SLOT INTERSECTION ────────────────────────────────────────────────
            // For each 30-minute candidate window we check whether EVERY user who is
            // "available today" actually covers that window in their time slots.
            // We search within the union of all windows (broadest range) but only
            // score a slot based on per-user coverage.

            var allSlots = usersAvailableToday.SelectMany(a => a.AvailableTimeSlots).ToList();
            if (allSlots.Count == 0) return suggestions;

            var searchStart = allSlots.Min(s => s.Start);
            var searchEnd   = allSlots.Max(s => s.End);

            var currentTime = searchStart;
            while (currentTime.AddMinutes(durationMinutes) <= searchEnd)
            {
                var slotEnd       = currentTime.AddMinutes(durationMinutes);
                var startDateTime = date.Add(currentTime.ToTimeSpan());
                var endDateTime   = date.Add(slotEnd.ToTimeSpan());

                int availableCount = 0;
                foreach (var userAvail in availabilities)
                {
                    // If user doesn't prefer this day, they're not available → don't count them
                    if (!userAvail.PreferredDays.Contains(dayOfWeek))
                        continue;

                    if (IsUserAvailableInSlot(userAvail, currentTime, slotEnd, date, existingMeetups))
                        availableCount++;
                }

                double score = availabilities.Count > 0
                    ? (double)availableCount / availabilities.Count * 100
                    : 0;

                // Include slot if at least half of ALL participants are free
                // (or at least 1 if it's a solo/two-person group)
                var threshold = availabilities.Count > 2
                    ? Math.Ceiling(availabilities.Count * 0.5)
                    : 1;

                if (availableCount >= threshold)
                {
                    suggestions.Add(new SuggestedTimeSlot
                    {
                        StartTime            = startDateTime,
                        EndTime              = endDateTime,
                        AvailableParticipants = availableCount,
                        TotalParticipants    = availabilities.Count,
                        AvailabilityScore    = score,
                        ConflictCount        = availabilities.Count - availableCount
                    });
                }

                currentTime = currentTime.AddMinutes(30);
            }

            return suggestions;
        }

        // ── Per-user slot check ────────────────────────────────────────────────────────
        private bool IsUserAvailableInSlot(
            UserAvailability userAvail,
            TimeOnly startTime,
            TimeOnly endTime,
            DateTime date,
            List<Meetup> existingMeetups)
        {
            // 1. The requested window must fall inside at least one of the user's time slots
            bool inAvailableWindow = userAvail.AvailableTimeSlots.Any(slot =>
                startTime >= slot.Start && endTime <= slot.End);

            if (!inAvailableWindow)
                return false;

            // 2. No conflicting meetup (using 2-hour default duration for existing meetups)
            var startDt = date.Add(startTime.ToTimeSpan());
            var endDt   = date.Add(endTime.ToTimeSpan());

            bool hasConflict = existingMeetups
                .Where(m => m.Status != "cancelled")
                .Any(m =>
                    m.EventDateTime < endDt &&
                    m.EventDateTime.AddHours(2) > startDt);

            return !hasConflict;
        }

        // ── Fetch existing meetups for conflict detection ───────────────────────────────
        private async Task<List<Meetup>> GetExistingMeetupsAsync(
            List<string> participantUserIds,
            DateTime startDate,
            DateTime endDate)
        {
            var meetups = new List<Meetup>();

            var startUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            var endUtc   = DateTime.SpecifyKind(endDate.Date.AddDays(1), DateTimeKind.Utc);

            var snapshot = await _db.Collection("meetups")
                                    .WhereGreaterThanOrEqualTo("EventDateTime", startUtc)
                                    .WhereLessThanOrEqualTo("EventDateTime", endUtc)
                                    .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var meetup = doc.ConvertTo<Meetup>();

                // Include the meetup if the creator is a participant
                if (participantUserIds.Contains(meetup.CreatorUserId))
                {
                    meetups.Add(meetup);
                    continue;
                }

                // Or if any invited participant is in the participants subcollection
                var participantsSnap = await doc.Reference
                    .Collection("participants")
                    .GetSnapshotAsync();

                if (participantsSnap.Documents.Any(p => participantUserIds.Contains(p.Id)))
                    meetups.Add(meetup);
            }

            return meetups;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────

        // Map .NET DayOfWeek enum to the display strings we store in Firestore
        private static string MapDayOfWeek(DayOfWeek dow) => dow switch
        {
            DayOfWeek.Monday    => "Monday",
            DayOfWeek.Tuesday   => "Tuesday",
            DayOfWeek.Wednesday => "Wednesday",
            DayOfWeek.Thursday  => "Thursday",
            DayOfWeek.Friday    => "Friday",
            DayOfWeek.Saturday  => "Saturday",
            DayOfWeek.Sunday    => "Sunday",
            _                   => dow.ToString()
        };

        private TimeSlot? ParseTimeSlot(object data)
        {
            if (data is Dictionary<string, object> dict &&
                dict.TryGetValue("Start", out var startObj) &&
                dict.TryGetValue("End",   out var endObj)   &&
                TimeOnly.TryParse(startObj?.ToString(), out var start) &&
                TimeOnly.TryParse(endObj?.ToString(),   out var end))
            {
                return new TimeSlot { Start = start, End = end };
            }
            return null;
        }
    }

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