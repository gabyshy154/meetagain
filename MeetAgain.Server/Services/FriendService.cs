using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class FriendService
    {
        private readonly FirestoreDb _db;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public FriendService(FirestoreDb db, AuthService auth, CurrentUserAccessor currentUser)
        {
            _db = db;
            _auth = auth;
            _currentUser = currentUser;
        }

        // Send a friend request
        public async Task<bool> SendFriendRequestAsync(string recipientEmail)
        {
            // Use CurrentUserAccessor for better reliability
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            
            // Fallback to AuthService if CurrentUserAccessor returns empty
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ SendFriendRequest: User not authenticated");
                return false;
            }

            try
            {
                Console.WriteLine($"📤 Sending friend request from {currentUserId} to {recipientEmail}");

                // Get current user info
                var currentUserDoc = await _db.Collection("users").Document(currentUserId).GetSnapshotAsync();
                if (!currentUserDoc.Exists)
                {
                    Console.WriteLine("❌ Current user document not found");
                    return false;
                }

                var currentUserData = currentUserDoc.ToDictionary();
                var currentUserEmail = currentUserData.ContainsKey("Email") ? currentUserData["Email"]?.ToString() ?? "" : "";
                var currentUserName = currentUserData.ContainsKey("DisplayName") ? currentUserData["DisplayName"]?.ToString() ?? "" : "";

                Console.WriteLine($"  └─ Current user: {currentUserName} ({currentUserEmail})");

                // Find recipient by email
                var usersQuery = _db.Collection("users")
                    .WhereEqualTo("Email", recipientEmail)
                    .Limit(1);
                var snapshot = await usersQuery.GetSnapshotAsync();

                if (snapshot.Documents.Count == 0)
                {
                    Console.WriteLine($"❌ Recipient not found: {recipientEmail}");
                    return false;
                }

                var recipientDoc = snapshot.Documents[0];
                var recipientId = recipientDoc.Id;

                Console.WriteLine($"  └─ Found recipient: {recipientId}");

                // Don't allow sending request to yourself
                if (recipientId == currentUserId)
                {
                    Console.WriteLine("❌ Cannot send friend request to yourself");
                    return false;
                }

                // Check if already friends
                var existingFriend = await _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friends")
                    .Document(recipientId)
                    .GetSnapshotAsync();

                if (existingFriend.Exists)
                {
                    Console.WriteLine("❌ Already friends");
                    return false;
                }

                // Check if request already exists
                var existingRequest = await _db.Collection("users")
                    .Document(recipientId)
                    .Collection("friendRequests")
                    .Document(currentUserId)
                    .GetSnapshotAsync();

                if (existingRequest.Exists)
                {
                    Console.WriteLine("❌ Friend request already sent");
                    return false;
                }

                // Create friend request
                var friendRequest = new Dictionary<string, object>
                {
                    { "FromUserId", currentUserId },
                    { "FromUserEmail", currentUserEmail },
                    { "FromUserName", currentUserName },
                    { "Status", "pending" },
                    { "SentAt", DateTime.UtcNow.ToString("o") }
                };

                Console.WriteLine($"  └─ Creating friend request document...");
                await _db.Collection("users")
                    .Document(recipientId)
                    .Collection("friendRequests")
                    .Document(currentUserId)
                    .SetAsync(friendRequest);

                Console.WriteLine("✅ Friend request saved to Firestore");

                // Create notification for recipient
                await CreateNotificationAsync(
                    recipientId,
                    "friend_request",
                    $"{currentUserName} sent you a friend request"
                );

                Console.WriteLine("✅ Notification sent");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending friend request: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Get all incoming friend requests
        public async Task<List<FriendRequest>> GetFriendRequestsAsync()
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ GetFriendRequests: User not authenticated");
                return new List<FriendRequest>();
            }

            try
            {
                Console.WriteLine($"📥 Getting friend requests for user: {currentUserId}");

                var snapshot = await _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friendRequests")
                    .WhereEqualTo("Status", "pending")
                    .GetSnapshotAsync();

                Console.WriteLine($"  └─ Found {snapshot.Count} pending request(s)");

                var requests = new List<FriendRequest>();
                foreach (var doc in snapshot.Documents)
                {
                    var data = doc.ToDictionary();
                    requests.Add(new FriendRequest
                    {
                        Id = doc.Id,
                        FromUserId = data.ContainsKey("FromUserId") ? data["FromUserId"]?.ToString() ?? "" : "",
                        FromUserEmail = data.ContainsKey("FromUserEmail") ? data["FromUserEmail"]?.ToString() ?? "" : "",
                        FromUserName = data.ContainsKey("FromUserName") ? data["FromUserName"]?.ToString() ?? "" : "",
                        Status = data.ContainsKey("Status") ? data["Status"]?.ToString() ?? "pending" : "pending",
                        SentAt = data.ContainsKey("SentAt") ? data["SentAt"]?.ToString() ?? "" : ""
                    });
                }

                return requests;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting friend requests: {ex.Message}");
                return new List<FriendRequest>();
            }
        }

        // Accept a friend request
        public async Task<bool> AcceptFriendRequestAsync(string fromUserId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ AcceptFriendRequest: User not authenticated");
                return false;
            }

            try
            {
                Console.WriteLine($"✅ Accepting friend request from {fromUserId} by {currentUserId}");

                // Get the friend request details
                var requestDoc = await _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friendRequests")
                    .Document(fromUserId)
                    .GetSnapshotAsync();

                if (!requestDoc.Exists)
                {
                    Console.WriteLine("❌ Friend request not found");
                    return false;
                }

                var requestData = requestDoc.ToDictionary();
                var friendName = requestData.ContainsKey("FromUserName") ? requestData["FromUserName"]?.ToString() ?? "" : "";
                var friendEmail = requestData.ContainsKey("FromUserEmail") ? requestData["FromUserEmail"]?.ToString() ?? "" : "";

                Console.WriteLine($"  └─ Request from: {friendName} ({friendEmail})");

                // Get current user's info
                var currentUserDoc = await _db.Collection("users").Document(currentUserId).GetSnapshotAsync();
                var currentUserData = currentUserDoc.ToDictionary();
                var currentUserName = currentUserData.ContainsKey("DisplayName") ? currentUserData["DisplayName"]?.ToString() ?? "" : "";
                var currentUserEmail = currentUserData.ContainsKey("Email") ? currentUserData["Email"]?.ToString() ?? "" : "";

                Console.WriteLine($"  └─ Current user: {currentUserName} ({currentUserEmail})");

                var batch = _db.StartBatch();

                // Add friend to current user's friends list
                var friendForCurrentUser = new Dictionary<string, object>
                {
                    { "Id", fromUserId },
                    { "Name", friendName },
                    { "Email", friendEmail },
                    { "AddedAt", DateTime.UtcNow.ToString("o") }
                };

                var currentUserFriendRef = _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friends")
                    .Document(fromUserId);
                
                Console.WriteLine($"  └─ Adding friend to {currentUserId}/friends/{fromUserId}");
                batch.Set(currentUserFriendRef, friendForCurrentUser);

                // Add current user to friend's friends list
                var friendForOtherUser = new Dictionary<string, object>
                {
                    { "Id", currentUserId },
                    { "Name", currentUserName },
                    { "Email", currentUserEmail },
                    { "AddedAt", DateTime.UtcNow.ToString("o") }
                };

                var otherUserFriendRef = _db.Collection("users")
                    .Document(fromUserId)
                    .Collection("friends")
                    .Document(currentUserId);
                
                Console.WriteLine($"  └─ Adding current user to {fromUserId}/friends/{currentUserId}");
                batch.Set(otherUserFriendRef, friendForOtherUser);

                // Delete the friend request
                var requestRef = _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friendRequests")
                    .Document(fromUserId);
                batch.Delete(requestRef);

                Console.WriteLine("  └─ Committing batch write...");
                await batch.CommitAsync();

                Console.WriteLine("✅ Batch committed successfully - Friends added to Firestore!");

                // Verify the write
                var verifyDoc = await _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friends")
                    .Document(fromUserId)
                    .GetSnapshotAsync();

                if (verifyDoc.Exists)
                {
                    Console.WriteLine("✅ VERIFIED: Friend document exists in Firestore");
                }
                else
                {
                    Console.WriteLine("⚠️ WARNING: Friend document not found after write!");
                }

                // Create notification for the friend
                await CreateNotificationAsync(
                    fromUserId,
                    "friend_accepted",
                    $"{currentUserName} accepted your friend request"
                );

                Console.WriteLine("✅ Friend request accepted successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error accepting friend request: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Reject a friend request
        public async Task<bool> RejectFriendRequestAsync(string fromUserId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ RejectFriendRequest: User not authenticated");
                return false;
            }

            try
            {
                Console.WriteLine($"🚫 Rejecting friend request from {fromUserId}");

                await _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friendRequests")
                    .Document(fromUserId)
                    .DeleteAsync();

                Console.WriteLine("✅ Friend request rejected");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error rejecting friend request: {ex.Message}");
                return false;
            }
        }

        // Get all friends - FIXED FOR HARD RELOAD
        public async Task<List<Friend>> GetFriendsAsync()
        {
            // CRITICAL FIX: Always wait for authentication first
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            
            // Fallback to AuthService
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ GetFriends: User not authenticated (waiting for auth state)");
                return new List<Friend>();
            }

            try
            {
                Console.WriteLine($"👥 Getting friends for user: {currentUserId}");
                Console.WriteLine($"  └─ Path: users/{currentUserId}/friends");

                var snapshot = await _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friends")
                    .GetSnapshotAsync();

                Console.WriteLine($"  └─ Found {snapshot.Count} friend document(s) in Firestore");

                var friends = new List<Friend>();
                foreach (var doc in snapshot.Documents)
                {
                    var friend = doc.ConvertTo<Friend>();
                    friends.Add(friend);
                    Console.WriteLine($"    • {friend.Name} ({friend.Email})");
                }

                Console.WriteLine($"✅ Returning {friends.Count} friend(s)");
                return friends.OrderBy(f => f.Name).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting friends: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<Friend>();
            }
        }

        // Remove a friend (unfriend)
        public async Task<bool> RemoveFriendAsync(string friendId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ RemoveFriend: User not authenticated");
                return false;
            }

            try
            {
                Console.WriteLine($"👋 Removing friendship between {currentUserId} and {friendId}");

                var batch = _db.StartBatch();

                // Remove friend from current user's friends list
                var currentUserFriendRef = _db.Collection("users")
                    .Document(currentUserId)
                    .Collection("friends")
                    .Document(friendId);
                batch.Delete(currentUserFriendRef);

                // Remove current user from friend's friends list
                var otherUserFriendRef = _db.Collection("users")
                    .Document(friendId)
                    .Collection("friends")
                    .Document(currentUserId);
                batch.Delete(otherUserFriendRef);

                await batch.CommitAsync();

                Console.WriteLine("✅ Friendship removed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error removing friend: {ex.Message}");
                return false;
            }
        }

        // Search for users by email (to add as friends)
        public async Task<AppUser?> SearchUserByEmailAsync(string email)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = _auth.UserId;
            }
            
            if (string.IsNullOrEmpty(currentUserId)) return null;

            try
            {
                var query = _db.Collection("users")
                    .WhereEqualTo("Email", email)
                    .Limit(1);

                var snapshot = await query.GetSnapshotAsync();
                
                if (snapshot.Documents.Count == 0)
                    return null;

                var doc = snapshot.Documents[0];
                var user = doc.ConvertTo<AppUser>();
                user.Uid = doc.Id;

                // Don't return current user
                if (user.Uid == currentUserId)
                    return null;

                return user;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error searching user: {ex.Message}");
                return null;
            }
        }

        // Helper method to create notifications
        private async Task CreateNotificationAsync(string userId, string type, string message)
        {
            try
            {
                var notificationId = Guid.NewGuid().ToString();
                var notification = new Dictionary<string, object>
                {
                    { "Id", notificationId },
                    { "Type", type },
                    { "Message", message },
                    { "CreatedAt", DateTime.UtcNow.ToString("o") },
                    { "IsRead", false },
                    { "MeetupId", "" },
                    { "FriendRequestId", "" },
                    { "GroupId", "" }
                };

                await _db.Collection("users")
                    .Document(userId)
                    .Collection("notifications")
                    .Document(notificationId)
                    .SetAsync(notification);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating notification: {ex.Message}");
            }
        }
    }
}