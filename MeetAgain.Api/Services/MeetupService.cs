using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class MeetupService
    {
        private readonly FirestoreDb _db;
        private readonly FirestoreService _fs;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public MeetupService(FirestoreDb db, FirestoreService fs, AuthService auth, CurrentUserAccessor currentUser)
        {
            _db = db;
            _fs = fs;
            _auth = auth;
            _currentUser = currentUser;
        }

        // ------------------------------------------------------
        // CREATE MEETUP (with optional participants)
        // ------------------------------------------------------
        public async Task<bool> CreateMeetupAsync(CreateMeetupModel model, List<string>? invitedFriendIds = null)
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrWhiteSpace(uid)) return false;

            try
            {
                // Get creator info from Firestore
                var userDoc = await _db.Collection("users").Document(uid).GetSnapshotAsync();
                if (!userDoc.Exists)
                {
                    Console.WriteLine($"User document not found for {uid}");
                    return false;
                }

                var userData = userDoc.ToDictionary();
                var creatorName = userData.ContainsKey("DisplayName") ? userData["DisplayName"]?.ToString() ?? "" : "";
                var creatorEmail = userData.ContainsKey("Email") ? userData["Email"]?.ToString() ?? "" : "";

                Console.WriteLine($"Creating meetup as {creatorName} ({uid})");

                // Combine date and time
                var eventDateTime = model.Date.Date.Add(model.Time.ToTimeSpan()).ToUniversalTime();

                // Create meetup object
                var meetup = new Meetup
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = model.Title,
                    Description = model.Description,
                    CreatorUserId = uid,
                    CreatorName = creatorName,
                    Location = "", // Empty for now, will be used in Phase 2
                    EventDateTime = eventDateTime,
                    CreatedAt = DateTime.UtcNow,
                    Status = "confirmed",
                    ParticipantCount = (invitedFriendIds?.Count ?? 0) + 1 // +1 for creator
                };

                Console.WriteLine($"Meetup ID: {meetup.Id}, ParticipantCount: {meetup.ParticipantCount}");

                // Save meetup to main collection
                await _db.Collection("meetups").Document(meetup.Id).SetAsync(meetup);
                Console.WriteLine("Meetup saved to Firestore");

                // Add creator as participant (auto-accepted)
                var creatorParticipant = new MeetupParticipant
                {
                    UserId = uid,
                    Name = creatorName,
                    Email = creatorEmail,
                    Status = "accepted",
                    InvitedAt = DateTime.UtcNow.ToString("o"),
                    RespondedAt = DateTime.UtcNow.ToString("o")
                };

                await _db.Collection("meetups")
                    .Document(meetup.Id)
                    .Collection("participants")
                    .Document(uid)
                    .SetAsync(creatorParticipant);

                Console.WriteLine("Creator added as participant");

                // Invite friends if specified
                if (invitedFriendIds != null && invitedFriendIds.Count > 0)
                {
                    Console.WriteLine($"Inviting {invitedFriendIds.Count} friends...");
                    await InviteFriendsToMeetupAsync(meetup.Id, invitedFriendIds, meetup.Title, creatorName);
                }

                Console.WriteLine($"✅ Meetup created successfully: {meetup.Id}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating meetup: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // ------------------------------------------------------
        // INVITE FRIENDS TO MEETUP (Private helper method)
        // ------------------------------------------------------
        private async Task<bool> InviteFriendsToMeetupAsync(string meetupId, List<string> friendIds, string meetupTitle, string creatorName)
        {
            try
            {
                var batch = _db.StartBatch();
                var invitedCount = 0;

                foreach (var friendId in friendIds)
                {
                    Console.WriteLine($"Processing invite for friend: {friendId}");

                    // Check if already invited
                    var existingParticipant = await _db.Collection("meetups")
                        .Document(meetupId)
                        .Collection("participants")
                        .Document(friendId)
                        .GetSnapshotAsync();

                    if (existingParticipant.Exists)
                    {
                        Console.WriteLine($"  └─ Friend {friendId} already invited, skipping");
                        continue;
                    }

                    // Get friend info
                    var friendDoc = await _db.Collection("users").Document(friendId).GetSnapshotAsync();
                    if (!friendDoc.Exists)
                    {
                        Console.WriteLine($"  └─ Friend {friendId} not found in users collection, skipping");
                        continue;
                    }

                    var friendData = friendDoc.ToDictionary();
                    var friendName = friendData.ContainsKey("DisplayName") ? friendData["DisplayName"]?.ToString() ?? "" : "";
                    var friendEmail = friendData.ContainsKey("Email") ? friendData["Email"]?.ToString() ?? "" : "";

                    Console.WriteLine($"  └─ Inviting: {friendName} ({friendEmail})");

                    // Create participant invite
                    var participant = new MeetupParticipant
                    {
                        UserId = friendId,
                        Name = friendName,
                        Email = friendEmail,
                        Status = "invited",
                        InvitedAt = DateTime.UtcNow.ToString("o"),
                        RespondedAt = ""
                    };

                    var participantRef = _db.Collection("meetups")
                        .Document(meetupId)
                        .Collection("participants")
                        .Document(friendId);
                    
                    batch.Set(participantRef, participant);
                    invitedCount++;

                    // Create notification (ENHANCED - includes metadata)
                    var notificationId = Guid.NewGuid().ToString();
                    var notification = new Dictionary<string, object>
                    {
                        { "Id", notificationId },
                        { "Type", "meetup_invite" },
                        { "Message", $"{creatorName} invited you to '{meetupTitle}'" },
                        { "MeetupId", meetupId },
                        { "CreatedAt", DateTime.UtcNow.ToString("o") },
                        { "IsRead", false },
                        { "FriendRequestId", "" },
                        { "GroupId", "" }
                    };

                    var notifRef = _db.Collection("users")
                        .Document(friendId)
                        .Collection("notifications")
                        .Document(notificationId);
                    
                    batch.Set(notifRef, notification);
                    Console.WriteLine($"  └─ Notification created for {friendName}");
                }

                await batch.CommitAsync();
                Console.WriteLine($"✅ Successfully invited {invitedCount} friends to meetup {meetupId}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error inviting friends: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // ------------------------------------------------------
        // RESPOND TO MEETUP (RSVP)
        // ------------------------------------------------------
        public async Task<bool> RespondToMeetupAsync(string meetupId, string status)
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrWhiteSpace(uid)) return false;

            try
            {
                var participantRef = _db.Collection("meetups")
                    .Document(meetupId)
                    .Collection("participants")
                    .Document(uid);

                var participantDoc = await participantRef.GetSnapshotAsync();
                if (!participantDoc.Exists)
                {
                    Console.WriteLine($"Participant {uid} not found for meetup {meetupId}");
                    return false;
                }

                // Get participant name for notification
                var participantData = participantDoc.ToDictionary();
                var userName = participantData.ContainsKey("Name") ? participantData["Name"]?.ToString() ?? "" : "";

                // Update RSVP status
                await participantRef.UpdateAsync(new Dictionary<string, object>
                {
                    { "Status", status },
                    { "RespondedAt", DateTime.UtcNow.ToString("o") }
                });

                // Get meetup details for notification
                var meetupDoc = await _db.Collection("meetups").Document(meetupId).GetSnapshotAsync();
                if (meetupDoc.Exists)
                {
                    var meetup = meetupDoc.ConvertTo<Meetup>();
                    
                    // Notify creator about RSVP change (only if not the creator themselves)
                    if (meetup.CreatorUserId != uid)
                    {
                        var notificationId = Guid.NewGuid().ToString();
                        var statusText = status switch
                        {
                            "accepted" => "accepted",
                            "declined" => "declined",
                            "maybe" => "responded 'maybe' to",
                            _ => "responded to"
                        };

                        var notification = new Dictionary<string, object>
                        {
                            { "Id", notificationId },
                            { "Type", "rsvp_change" },
                            { "Message", $"{userName} {statusText} your meetup '{meetup.Title}'" },
                            { "MeetupId", meetupId },
                            { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false },
                            { "FriendRequestId", "" },
                            { "GroupId", "" }
                        };

                        await _db.Collection("users")
                            .Document(meetup.CreatorUserId)
                            .Collection("notifications")
                            .Document(notificationId)
                            .SetAsync(notification);
                        
                        Console.WriteLine($"Notified creator about RSVP change");
                    }
                }

                Console.WriteLine($"✅ User {uid} responded to meetup {meetupId} with status: {status}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error responding to meetup: {ex.Message}");
                return false;
            }
        }

        // ------------------------------------------------------
        // GET MY MEETUPS (created + invited)
        // ------------------------------------------------------
        public async Task<List<MeetupDto>> GetMyMeetupsAsync()
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrWhiteSpace(uid)) return new List<MeetupDto>();

            try
            {
                Console.WriteLine($"Getting meetups for user: {uid}");
                var meetups = new List<MeetupDto>();

                // Get all meetups
                var allMeetupsSnapshot = await _db.Collection("meetups").GetSnapshotAsync();
                Console.WriteLine($"Found {allMeetupsSnapshot.Count} total meetups in database");
                
                foreach (var meetupDoc in allMeetupsSnapshot.Documents)
                {
                    var meetup = meetupDoc.ConvertTo<Meetup>();
                    var meetupId = meetupDoc.Id;
                    
                    // Check if I'm a participant
                    var participantDoc = await _db.Collection("meetups")
                        .Document(meetupId)
                        .Collection("participants")
                        .Document(uid)
                        .GetSnapshotAsync();

                    if (participantDoc.Exists)
                    {
                        var participant = participantDoc.ConvertTo<MeetupParticipant>();

                        meetups.Add(new MeetupDto
                        {
                            Id = meetup.Id,
                            Title = meetup.Title,
                            Description = meetup.Description,
                            CreatorUserId = meetup.CreatorUserId,
                            CreatorName = meetup.CreatorName,
                            Location = meetup.Location,
                            EventDateTime = meetup.EventDateTime,
                            CreatedAt = meetup.CreatedAt,
                            Status = meetup.Status,
                            ParticipantCount = meetup.ParticipantCount,
                            IsCreator = meetup.CreatorUserId == uid,
                            MyRSVPStatus = participant.Status
                        });

                        Console.WriteLine($"  └─ Added meetup: {meetup.Title} (Status: {participant.Status})");
                    }
                }

                Console.WriteLine($"✅ Returning {meetups.Count} meetups for user");
                return meetups.OrderBy(m => m.EventDateTime).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting meetups: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<MeetupDto>();
            }
        }

        // ------------------------------------------------------
        // GET MEETUP DETAILS (with participants)
        // ------------------------------------------------------
        public async Task<MeetupDetailDto?> GetMeetupDetailAsync(string meetupId)
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrWhiteSpace(uid)) return null;

            try
            {
                var meetupDoc = await _db.Collection("meetups").Document(meetupId).GetSnapshotAsync();
                if (!meetupDoc.Exists)
                {
                    Console.WriteLine($"Meetup {meetupId} not found");
                    return null;
                }

                var meetup = meetupDoc.ConvertTo<Meetup>();

                // Get all participants
                var participantsSnapshot = await _db.Collection("meetups")
                    .Document(meetupId)
                    .Collection("participants")
                    .GetSnapshotAsync();

                var participants = new List<MeetupParticipant>();
                string myRSVPStatus = "";

                foreach (var doc in participantsSnapshot.Documents)
                {
                    var participant = doc.ConvertTo<MeetupParticipant>();
                    participants.Add(participant);

                    if (participant.UserId == uid)
                    {
                        myRSVPStatus = participant.Status;
                    }
                }

                Console.WriteLine($"✅ Loaded meetup {meetupId} with {participants.Count} participants");

                return new MeetupDetailDto
                {
                    Meetup = meetup,
                    Participants = participants.OrderBy(p => p.Name).ToList(),
                    IsCreator = meetup.CreatorUserId == uid,
                    MyRSVPStatus = myRSVPStatus
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting meetup detail: {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------
        // DELETE MEETUP (with notifications)
        // ------------------------------------------------------
        public async Task<bool> DeleteMeetupAsync(string meetupId)
        {
            var (uid, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrWhiteSpace(uid)) return false;

            try
            {
                Console.WriteLine($"Attempting to delete meetup: {meetupId}");

                var meetupDoc = await _db.Collection("meetups").Document(meetupId).GetSnapshotAsync();
                if (!meetupDoc.Exists)
                {
                    Console.WriteLine("Meetup not found");
                    return false;
                }

                var meetup = meetupDoc.ConvertTo<Meetup>();
                if (meetup.CreatorUserId != uid)
                {
                    Console.WriteLine($"User {uid} is not the creator, cannot delete");
                    return false; // Only creator can delete
                }

                // Get all participants to notify them
                var participantsSnapshot = await _db.Collection("meetups")
                    .Document(meetupId)
                    .Collection("participants")
                    .GetSnapshotAsync();

                Console.WriteLine($"Deleting {participantsSnapshot.Count} participants");

                var batch = _db.StartBatch();
                
                // Delete participants and send notifications
                foreach (var doc in participantsSnapshot.Documents)
                {
                    var participant = doc.ConvertTo<MeetupParticipant>();
                    
                    // Don't notify the creator (themselves)
                    if (participant.UserId != uid)
                    {
                        // Create cancellation notification
                        var notificationId = Guid.NewGuid().ToString();
                        var notification = new Dictionary<string, object>
                        {
                            { "Id", notificationId },
                            { "Type", "meetup_update" },
                            { "Message", $"'{meetup.Title}' has been cancelled" },
                            { "MeetupId", meetupId },
                            { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false },
                            { "FriendRequestId", "" },
                            { "GroupId", "" }
                        };

                        var notifRef = _db.Collection("users")
                            .Document(participant.UserId)
                            .Collection("notifications")
                            .Document(notificationId);
                        
                        batch.Set(notifRef, notification);
                    }
                    
                    batch.Delete(doc.Reference);
                }

                // Delete the meetup document
                batch.Delete(_db.Collection("meetups").Document(meetupId));
                await batch.CommitAsync();

                Console.WriteLine($"✅ Deleted meetup: {meetupId}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting meetup: {ex.Message}");
                return false;
            }
        }

        // ------------------------------------------------------
        // UPDATE MEETUP (with notifications)
        // ------------------------------------------------------
        public async Task<bool> UpdateMeetupAsync(Meetup meetup)
        {
            if (meetup == null || string.IsNullOrWhiteSpace(meetup.Id))
                return false;

            var (uid, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrWhiteSpace(uid) || meetup.CreatorUserId != uid)
                return false;

            try
            {
                await _db.Collection("meetups").Document(meetup.Id).SetAsync(meetup);
                Console.WriteLine($"✅ Updated meetup: {meetup.Id}");
                
                // Notify all participants about the update
                var participantsSnapshot = await _db.Collection("meetups")
                    .Document(meetup.Id)
                    .Collection("participants")
                    .GetSnapshotAsync();

                var batch = _db.StartBatch();
                foreach (var doc in participantsSnapshot.Documents)
                {
                    var participant = doc.ConvertTo<MeetupParticipant>();
                    
                    // Don't notify the creator (themselves)
                    if (participant.UserId != uid && 
                        (participant.Status == "accepted" || participant.Status == "invited"))
                    {
                        var notificationId = Guid.NewGuid().ToString();
                        var notification = new Dictionary<string, object>
                        {
                            { "Id", notificationId },
                            { "Type", "meetup_update" },
                            { "Message", $"'{meetup.Title}' has been updated" },
                            { "MeetupId", meetup.Id },
                            { "CreatedAt", DateTime.UtcNow.ToString("o") },
                            { "IsRead", false },
                            { "FriendRequestId", "" },
                            { "GroupId", "" }
                        };

                        var notifRef = _db.Collection("users")
                            .Document(participant.UserId)
                            .Collection("notifications")
                            .Document(notificationId);
                        
                        batch.Set(notifRef, notification);
                    }
                }
                
                await batch.CommitAsync();
                Console.WriteLine($"✅ Sent update notifications to participants");
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating meetup: {ex.Message}");
                return false;
            }
        }
    }
}