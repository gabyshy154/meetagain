using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class NotificationService
    {
        private readonly FirestoreDb _db;
        private readonly CurrentUserAccessor _currentUserAccessor;

        public NotificationService(FirestoreDb db, CurrentUserAccessor currentUserAccessor)
        {
            _db = db;
            _currentUserAccessor = currentUserAccessor;
        }

        // Create a notification for a user
        public async Task CreateNotificationAsync(string userId, string type, string message, Dictionary<string, object>? metadata = null)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Message = message,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                IsRead = false
            };

            var docRef = _db.Collection("users").Document(userId)
                           .Collection("notifications").Document(notification.Id);

            var data = new Dictionary<string, object>
            {
                { "Id", notification.Id },
                { "Type", type },
                { "Message", message },
                { "CreatedAt", notification.CreatedAt },
                { "IsRead", false }
            };

            // Add metadata if provided (meetupId, friendRequestId, etc.)
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    data[kvp.Key] = kvp.Value;
                }
            }

            await docRef.SetAsync(data);
        }

        // Get all notifications for current user
        public async Task<List<NotificationDto>> GetMyNotificationsAsync()
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return new List<NotificationDto>();

            var snapshot = await _db.Collection("users").Document(userId)
                                   .Collection("notifications")
                                   .OrderByDescending("CreatedAt")
                                   .Limit(50)
                                   .GetSnapshotAsync();

            var notifications = new List<NotificationDto>();
            foreach (var doc in snapshot.Documents)
            {
                var data = doc.ToDictionary();
                
                // Use document ID if Id field is not set
                var notificationId = data.ContainsKey("Id") && !string.IsNullOrEmpty(data["Id"]?.ToString()) 
                    ? data["Id"].ToString() 
                    : doc.Id;
                
                notifications.Add(new NotificationDto
                {
                    Id = notificationId ?? "",
                    Type = data.ContainsKey("Type") ? data["Type"]?.ToString() ?? "" : "",
                    Message = data.ContainsKey("Message") ? data["Message"]?.ToString() ?? "" : "",
                    CreatedAt = data.ContainsKey("CreatedAt") ? data["CreatedAt"]?.ToString() ?? "" : "",
                    IsRead = data.ContainsKey("IsRead") && (bool)data["IsRead"],
                    MeetupId = data.ContainsKey("MeetupId") ? data["MeetupId"]?.ToString() : null,
                    FriendRequestId = data.ContainsKey("FriendRequestId") ? data["FriendRequestId"]?.ToString() : null,
                    GroupId = data.ContainsKey("GroupId") ? data["GroupId"]?.ToString() : null
                });
            }

            return notifications;
        }

        // Get unread notification count
        public async Task<int> GetUnreadCountAsync()
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return 0;

            var snapshot = await _db.Collection("users").Document(userId)
                                   .Collection("notifications")
                                   .WhereEqualTo("IsRead", false)
                                   .GetSnapshotAsync();

            return snapshot.Count;
        }

        // Mark notification as read
        public async Task MarkAsReadAsync(string notificationId)
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return;

            var docRef = _db.Collection("users").Document(userId)
                           .Collection("notifications").Document(notificationId);

            await docRef.UpdateAsync("IsRead", true);
        }

        // Mark all notifications as read
        public async Task MarkAllAsReadAsync()
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return;

            var snapshot = await _db.Collection("users").Document(userId)
                                   .Collection("notifications")
                                   .WhereEqualTo("IsRead", false)
                                   .GetSnapshotAsync();

            var batch = _db.StartBatch();
            foreach (var doc in snapshot.Documents)
            {
                batch.Update(doc.Reference, "IsRead", true);
            }

            await batch.CommitAsync();
        }

        // Delete a notification
        public async Task DeleteNotificationAsync(string notificationId)
        {
            var (userId, _) = await _currentUserAccessor.GetUserAsync();
            if (string.IsNullOrEmpty(userId)) return;

            await _db.Collection("users").Document(userId)
                    .Collection("notifications").Document(notificationId)
                    .DeleteAsync();
        }

        // Notification helper methods for common scenarios
        public async Task NotifyMeetupInviteAsync(string userId, string meetupTitle, string creatorName, string meetupId)
        {
            var message = $"{creatorName} invited you to '{meetupTitle}'";
            await CreateNotificationAsync(userId, "meetup_invite", message, new Dictionary<string, object>
            {
                { "MeetupId", meetupId }
            });
        }

        public async Task NotifyMeetupUpdateAsync(string userId, string meetupTitle, string updateType, string meetupId)
        {
            var message = $"'{meetupTitle}' has been {updateType}";
            await CreateNotificationAsync(userId, "meetup_update", message, new Dictionary<string, object>
            {
                { "MeetupId", meetupId }
            });
        }

        public async Task NotifyFriendRequestAsync(string userId, string fromUserName, string requestId)
        {
            var message = $"{fromUserName} sent you a friend request";
            await CreateNotificationAsync(userId, "friend_request", message, new Dictionary<string, object>
            {
                { "FriendRequestId", requestId }
            });
        }

        public async Task NotifyFriendRequestAcceptedAsync(string userId, string acceptedByName)
        {
            var message = $"{acceptedByName} accepted your friend request";
            await CreateNotificationAsync(userId, "friend_request_accepted", message);
        }

        public async Task NotifyGroupInviteAsync(string userId, string groupName, string invitedByName, string groupId)
        {
            var message = $"{invitedByName} added you to the group '{groupName}'";
            await CreateNotificationAsync(userId, "group_invite", message, new Dictionary<string, object>
            {
                { "GroupId", groupId }
            });
        }

        public async Task NotifyRSVPChangeAsync(string creatorUserId, string userName, string meetupTitle, string status, string meetupId)
        {
            var message = $"{userName} {status} your meetup '{meetupTitle}'";
            await CreateNotificationAsync(creatorUserId, "rsvp_change", message, new Dictionary<string, object>
            {
                { "MeetupId", meetupId }
            });
        }
    }

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