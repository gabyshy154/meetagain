using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MeetAgain.Server.Services
{
    public class FirestoreService
    {
        private readonly FirestoreDb _db;
        public CustomAuthStateProvider? AuthStateProvider { get; set; }

        public FirestoreService(FirestoreDb firestoreDb)
        {
            _db = firestoreDb ?? throw new ArgumentNullException(nameof(firestoreDb));
        }

        // ------------------------------------------------------
        // USERS
        // ------------------------------------------------------
        public async Task<AppUser?> GetUserAsync(string userId)
        {
            var doc = await _db.Collection("users").Document(userId).GetSnapshotAsync();
            if (!doc.Exists) return null;

            var user = doc.ConvertTo<AppUser>();
            user.Uid = doc.Id;
            return user;
        }

        public async Task<List<AppUser>> GetAllUsersAsync()
        {
            var snap = await _db.Collection("users").GetSnapshotAsync();
            return snap.Documents.Select(d =>
            {
                var u = d.ConvertTo<AppUser>();
                u.Uid = d.Id;
                return u;
            }).ToList();
        }

        public async Task CreateOrUpdateUserAsync(AppUser user)
        {
            if (string.IsNullOrWhiteSpace(user.Uid))
                throw new ArgumentException("User must have Uid.");

            await _db.Collection("users")
                     .Document(user.Uid)
                     .SetAsync(user, SetOptions.MergeAll);
        }

        // ------------------------------------------------------
        // MEETUPS
        // ------------------------------------------------------
        public async Task<string> CreateMeetupAsync(Meetup meetup)
        {
            if (string.IsNullOrWhiteSpace(meetup.Id))
                meetup.Id = NewId();

            if (meetup.CreatedAt == default)
                meetup.CreatedAt = DateTime.UtcNow;

            await _db.Collection("meetups").Document(meetup.Id)
                     .SetAsync(meetup, SetOptions.MergeAll);

            return meetup.Id;
        }

        public async Task<Meetup?> GetMeetupByIdAsync(string meetupId)
        {
            var doc = await _db.Collection("meetups").Document(meetupId).GetSnapshotAsync();
            if (!doc.Exists) return null;

            var meetup = doc.ConvertTo<Meetup>();
            meetup.Id = doc.Id;
            return meetup;
        }

        public async Task<List<MeetupDto>> GetUserMeetupsAsync(string userId)
        {
            var query = _db.Collection("meetups")
                           .WhereEqualTo("CreatorUserId", userId)
                           .OrderBy("EventDateTime");

            var snap = await query.GetSnapshotAsync();

            return snap.Documents.Select(doc =>
            {
                var m = doc.ConvertTo<Meetup>();
                m.Id = doc.Id;

                return new MeetupDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    Description = m.Description,
                    EventDateTime = m.EventDateTime
                };
            }).ToList();
        }

        // ------------------------------------------------------
        // DELETE MEETUP (original)
        // ------------------------------------------------------
        public Task DeleteMeetupAsync(string meetupId)
        {
            if (string.IsNullOrWhiteSpace(meetupId)) return Task.CompletedTask;
            return _db.Collection("meetups").Document(meetupId).DeleteAsync();
        }

        // ------------------------------------------------------
        // DELETE MEETUP (NEW OVERLOAD) 
        // ------------------------------------------------------
        public Task DeleteMeetupAsync(string userId, string meetupId)
        {
            // You DO NOT use users/{userId}/meetups/...
            // Your meetups are stored in root "meetups" collection.
            // So ignore userId and delete normally.
            if (string.IsNullOrWhiteSpace(meetupId)) return Task.CompletedTask;
            return _db.Collection("meetups").Document(meetupId).DeleteAsync();
        }

        // ------------------------------------------------------
        // UPDATE
        // ------------------------------------------------------
        public async Task UpdateMeetupAsync(Meetup meetup)
        {
            if (string.IsNullOrWhiteSpace(meetup.Id))
                throw new ArgumentException("Meetup ID is required.");

            await _db.Collection("meetups")
                     .Document(meetup.Id)
                     .SetAsync(meetup, SetOptions.MergeAll);
        }

        // ------------------------------------------------------
        // FRIENDS
        // ------------------------------------------------------
        public async Task AddFriendAsync(Friend friend)
        {
            if (string.IsNullOrWhiteSpace(friend.Id))
                friend.Id = NewId();

            if (string.IsNullOrWhiteSpace(friend.AddedAt))
                friend.AddedAt = DateTime.UtcNow.ToString("o");

            await _db.Collection("friends").Document(friend.Id)
                     .SetAsync(friend, SetOptions.MergeAll);
        }

        public async Task<List<Friend>> GetFriendsAsync(string userId)
        {
            var snap = await _db.Collection("friends")
                                .WhereEqualTo("Id", userId)
                                .GetSnapshotAsync();

            return snap.Documents.Select(d => d.ConvertTo<Friend>()).ToList();
        }

        public Task RemoveFriendAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId)) return Task.CompletedTask;
            return _db.Collection("friends").Document(friendId).DeleteAsync();
        }

        // ------------------------------------------------------
        // GROUPS
        // ------------------------------------------------------
        public async Task CreateGroupAsync(Group group)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
                group.Id = NewId();

            if (string.IsNullOrWhiteSpace(group.CreatedAt))
                group.CreatedAt = DateTime.UtcNow.ToString("o");

            await _db.Collection("groups").Document(group.Id)
                     .SetAsync(group, SetOptions.MergeAll);
        }

        public async Task<List<Group>> GetGroupsByOwnerAsync(string ownerId)
        {
            var snap = await _db.Collection("groups")
                                .WhereEqualTo("OwnerId", ownerId)
                                .GetSnapshotAsync();

            return snap.Documents.Select(d => d.ConvertTo<Group>()).ToList();
        }

        public async Task AddMemberAsync(string groupId, GroupMember member)
        {
            if (string.IsNullOrWhiteSpace(member.AddedAt))
                member.AddedAt = DateTime.UtcNow.ToString("o");

            await _db.Collection("groups").Document(groupId)
                     .Collection("members").Document(member.UserId)
                     .SetAsync(member, SetOptions.MergeAll);
        }

        public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId)
        {
            var snap = await _db.Collection("groups")
                                .Document(groupId)
                                .Collection("members")
                                .GetSnapshotAsync();

            return snap.Documents.Select(d => d.ConvertTo<GroupMember>()).ToList();
        }

        // ------------------------------------------------------
        // UTILS
        // ------------------------------------------------------
        public string NewId() => Guid.NewGuid().ToString("N");
    }
}
