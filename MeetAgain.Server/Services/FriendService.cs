using MeetAgain.Server.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MeetAgain.Server.Services
{
    public class FriendService
    {
        private readonly FirestoreService _fs;
        private readonly AuthService _auth;

        public FriendService(FirestoreService fs, AuthService auth)
        {
            _fs = fs;
            _auth = auth;
        }

        public async Task<List<Friend>> GetFriendsAsync()
        {
            var myUserId = _auth.UserId;
            if (myUserId == null) return new List<Friend>();
            return await _fs.GetFriendsAsync(myUserId);
        }

        public async Task<bool> RemoveFriendAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId)) return false;
            try
            {
                await _fs.RemoveFriendAsync(friendId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendFriendRequestAsync(string toEmail)
        {
            var myUserId = _auth.UserId;
            if (myUserId == null) return false;

            // Look up user by email
            var toUser = await _fs.GetUserByEmailAsync(toEmail);
            if (toUser == null) return false;

            // Don't send to yourself
            if (toUser.Uid == myUserId) return false;

            // Check if already friends or request already sent
            var existing = await _fs.GetFriendRequestAsync(myUserId, toUser.Uid);
            if (existing != null) return false;

            var me = await _fs.GetUserAsync(myUserId);

            var request = new FriendRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                FromUserId = myUserId,
                FromUserName = me?.DisplayName ?? "",
                FromUserEmail = me?.Email ?? "",
                ToUserId = toUser.Uid,
                Status = "pending",
                SentAt = DateTime.UtcNow.ToString("o")
            };

            await _fs.SendFriendRequestAsync(request);
            return true;
        }

        public async Task<List<FriendRequest>> GetFriendRequestsAsync()
        {
            var myUserId = _auth.UserId;
            if (myUserId == null) return new List<FriendRequest>();
            return await _fs.GetFriendRequestsAsync(myUserId);
        }

        public async Task<bool> AcceptFriendRequestAsync(string fromUserId)
        {
            var myUserId = _auth.UserId;
            if (myUserId == null) return false;

            var request = await _fs.GetFriendRequestAsync(fromUserId, myUserId);
            if (request == null) return false;

            var fromUser = await _fs.GetUserAsync(fromUserId);
            var me = await _fs.GetUserAsync(myUserId);

            // Add both directions
            await _fs.AddFriendAsync(new Friend
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = myUserId,
                FriendUserId = fromUserId,
                Name = fromUser?.DisplayName ?? "",
                Email = fromUser?.Email ?? "",
                AddedAt = DateTime.UtcNow.ToString("o")
            });

            await _fs.AddFriendAsync(new Friend
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = fromUserId,
                FriendUserId = myUserId,
                Name = me?.DisplayName ?? "",
                Email = me?.Email ?? "",
                AddedAt = DateTime.UtcNow.ToString("o")
            });

            await _fs.DeleteFriendRequestAsync(request.Id);
            return true;
        }

        public async Task<bool> RejectFriendRequestAsync(string fromUserId)
        {
            var myUserId = _auth.UserId;
            if (myUserId == null) return false;

            var request = await _fs.GetFriendRequestAsync(fromUserId, myUserId);
            if (request == null) return false;

            await _fs.DeleteFriendRequestAsync(request.Id);
            return true;
        }
    }
}