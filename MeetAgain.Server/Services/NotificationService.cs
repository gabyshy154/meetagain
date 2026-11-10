using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class NotificationService
    {
        private readonly FirestoreDb _db;
        private readonly CurrentUserAccessor _currentUserAccessor;
        private readonly ApiClient _api;   // NEW – Stage C2

        public NotificationService(FirestoreDb db, CurrentUserAccessor currentUserAccessor, ApiClient api)
        {
            _db = db;
            _currentUserAccessor = currentUserAccessor;
            _api = api;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage C2: the five methods the pages use now go through the API.
        // The API does the Firestore work and works out the user from the token.
        // ─────────────────────────────────────────────────────────────────────

        // Get all notifications for current user
        public async Task<List<NotificationDto>> GetMyNotificationsAsync()
        {
            return await _api.GetAsync<List<NotificationDto>>("api/notifications")
                   ?? new List<NotificationDto>();
        }

        // Get unread notification count
        public async Task<int> GetUnreadCountAsync()
        {
            return await _api.GetAsync<int>("api/notifications/unread-count");
        }

        // Mark notification as read
        public async Task MarkAsReadAsync(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId)) return;
            await _api.SendAsync(HttpMethod.Post,
                $"api/notifications/{Uri.EscapeDataString(notificationId)}/read");
        }

        // Mark all notifications as read
        public async Task MarkAllAsReadAsync()
        {
            await _api.SendAsync(HttpMethod.Post, "api/notifications/read-all");
        }

        // Delete a notification
        public async Task DeleteNotificationAsync(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId)) return;
            await _api.SendAsync(HttpMethod.Delete,
                $"api/notifications/{Uri.EscapeDataString(notificationId)}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // UNCHANGED for now: still used by the web-side Friend/Group/Meetup
        // services. These disappear from the web app when those services move.
        // ─────────────────────────────────────────────────────────────────────

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
}
