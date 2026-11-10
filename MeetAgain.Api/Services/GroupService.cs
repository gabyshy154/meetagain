using Google.Cloud.Firestore;
using MeetAgain.Server.Models;

namespace MeetAgain.Server.Services
{
    public class GroupService
    {
        private readonly FirestoreDb _db;
        private readonly AuthService _auth;
        private readonly CurrentUserAccessor _currentUser;

        public GroupService(FirestoreDb db, AuthService auth, CurrentUserAccessor currentUser)
        {
            _db = db;
            _auth = auth;
            _currentUser = currentUser;
        }

        // ─── CREATE GROUP ────────────────────────────────────────────────────────────
        public async Task<string?> CreateGroupAsync(CreateGroupModel model)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
                currentUserId = _auth.UserId;

            if (string.IsNullOrEmpty(currentUserId)) return null;

            try
            {
                var userDoc = await _db.Collection("users").Document(currentUserId).GetSnapshotAsync();
                if (!userDoc.Exists) return null;

                var userData = userDoc.ToDictionary();
                var ownerName  = userData.GetValueOrDefault("DisplayName")?.ToString() ?? "";
                var ownerEmail = userData.GetValueOrDefault("Email")?.ToString() ?? "";

                var groupId = Guid.NewGuid().ToString("N");

                // FIX: MemberCount starts at 1 (the owner counts as a member)
                var initialFriendCount = model.InitialMemberIds?.Count ?? 0;
                var group = new Group
                {
                    Id          = groupId,
                    OwnerId     = currentUserId,
                    OwnerName   = ownerName,
                    Name        = model.Name,
                    Description = model.Description,
                    MemberCount = 1 + initialFriendCount,   // owner + invited friends
                    CreatedAt   = DateTime.UtcNow.ToString("o")
                };

                await _db.Collection("groups").Document(groupId).SetAsync(group);
                Console.WriteLine($"✅ Group created: {groupId}");

                // FIX: Add the owner themselves as a member so they appear in the list
                var ownerMember = new GroupMember
                {
                    UserId  = currentUserId,
                    Name    = ownerName,
                    Email   = ownerEmail,
                    AddedAt = DateTime.UtcNow.ToString("o"),
                    AddedBy = currentUserId
                };

                await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .Document(currentUserId)
                    .SetAsync(ownerMember);

                Console.WriteLine($"  └─ Owner added as member: {ownerName}");

                // Add any selected friends
                if (model.InitialMemberIds != null && model.InitialMemberIds.Count > 0)
                {
                    await AddMembersToGroupAsync(groupId, model.InitialMemberIds, skipOwnerCheck: true);
                }

                return groupId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating group: {ex.Message}");
                return null;
            }
        }

        // ─── GET MY GROUPS ───────────────────────────────────────────────────────────
        public async Task<List<GroupDto>> GetMyGroupsAsync()
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
                currentUserId = _auth.UserId;

            if (string.IsNullOrEmpty(currentUserId))
            {
                Console.WriteLine("❌ GetMyGroups: User not authenticated");
                return new List<GroupDto>();
            }

            try
            {
                Console.WriteLine($"👥 Getting groups for user: {currentUserId}");

                var groups = new List<GroupDto>();
                var allGroupsSnapshot = await _db.Collection("groups").GetSnapshotAsync();

                Console.WriteLine($"  └─ Found {allGroupsSnapshot.Count} total group(s) in Firestore");

                foreach (var groupDoc in allGroupsSnapshot.Documents)
                {
                    var group = groupDoc.ConvertTo<Group>();

                    // Check if user is a member (owner is now always a member, so one check covers both)
                    var memberDoc = await _db.Collection("groups")
                        .Document(group.Id)
                        .Collection("members")
                        .Document(currentUserId)
                        .GetSnapshotAsync();

                    if (!memberDoc.Exists) continue;

                    groups.Add(new GroupDto
                    {
                        Id          = group.Id,
                        OwnerId     = group.OwnerId,
                        OwnerName   = group.OwnerName,
                        Name        = group.Name,
                        Description = group.Description,
                        MemberCount = group.MemberCount,
                        CreatedAt   = group.CreatedAt,
                        IsOwner     = group.OwnerId == currentUserId
                    });

                    Console.WriteLine($"  └─ Added group: {group.Name} (owner: {group.OwnerId == currentUserId})");
                }

                Console.WriteLine($"✅ Returning {groups.Count} group(s)");
                return groups.OrderBy(g => g.Name).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting groups: {ex.Message}");
                return new List<GroupDto>();
            }
        }

        // ─── GET GROUP DETAILS ───────────────────────────────────────────────────────
        public async Task<GroupDto?> GetGroupDetailAsync(string groupId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
                currentUserId = _auth.UserId;

            if (string.IsNullOrEmpty(currentUserId)) return null;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return null;

                var group = groupDoc.ConvertTo<Group>();

                var membersSnapshot = await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .GetSnapshotAsync();

                var members = new List<GroupMember>();
                foreach (var doc in membersSnapshot.Documents)
                {
                    members.Add(doc.ConvertTo<GroupMember>());
                }

                return new GroupDto
                {
                    Id          = group.Id,
                    OwnerId     = group.OwnerId,
                    OwnerName   = group.OwnerName,
                    Name        = group.Name,
                    Description = group.Description,
                    MemberCount = group.MemberCount,
                    CreatedAt   = group.CreatedAt,
                    IsOwner     = group.OwnerId == currentUserId,
                    // FIX: Sort so the owner always appears first, then alphabetically
                    Members     = members
                        .OrderBy(m => m.UserId == group.OwnerId ? 0 : 1)
                        .ThenBy(m => m.Name)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting group detail: {ex.Message}");
                return null;
            }
        }

        // ─── GET GROUP BY ID (used by CreateMeetup) ──────────────────────────────────
        public async Task<GroupDto?> GetGroupByIdAsync(string groupId)
        {
            return await GetGroupDetailAsync(groupId);
        }

        // ─── ADD MEMBERS TO GROUP ────────────────────────────────────────────────────
        // skipOwnerCheck = true when called from CreateGroupAsync (owner check already done)
        public async Task<bool> AddMembersToGroupAsync(string groupId, List<string> friendIds,
            bool skipOwnerCheck = false)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
                currentUserId = _auth.UserId;

            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return false;

                var group = groupDoc.ConvertTo<Group>();
                if (!skipOwnerCheck && group.OwnerId != currentUserId) return false;

                var batch = _db.StartBatch();
                var addedCount = 0;

                foreach (var friendId in friendIds)
                {
                    var existingMember = await _db.Collection("groups")
                        .Document(groupId)
                        .Collection("members")
                        .Document(friendId)
                        .GetSnapshotAsync();

                    if (existingMember.Exists) continue;

                    var friendDoc = await _db.Collection("users").Document(friendId).GetSnapshotAsync();
                    if (!friendDoc.Exists) continue;

                    var friendData = friendDoc.ToDictionary();
                    var member = new GroupMember
                    {
                        UserId  = friendId,
                        Name    = friendData.GetValueOrDefault("DisplayName")?.ToString() ?? "",
                        Email   = friendData.GetValueOrDefault("Email")?.ToString() ?? "",
                        AddedAt = DateTime.UtcNow.ToString("o"),
                        AddedBy = currentUserId
                    };

                    batch.Set(
                        _db.Collection("groups").Document(groupId).Collection("members").Document(friendId),
                        member);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    batch.Update(_db.Collection("groups").Document(groupId), new Dictionary<string, object>
                    {
                        { "MemberCount", group.MemberCount + addedCount }
                    });
                }

                await batch.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error adding members: {ex.Message}");
                return false;
            }
        }

        // ─── REMOVE MEMBER ───────────────────────────────────────────────────────────
        public async Task<bool> RemoveMemberAsync(string groupId, string memberId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
                currentUserId = _auth.UserId;

            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return false;

                var group = groupDoc.ConvertTo<Group>();
                if (group.OwnerId != currentUserId) return false;

                // FIX: Prevent the owner from being removed from the group
                if (memberId == group.OwnerId)
                {
                    Console.WriteLine("❌ Cannot remove the group owner from members");
                    return false;
                }

                var batch = _db.StartBatch();
                batch.Delete(_db.Collection("groups").Document(groupId).Collection("members").Document(memberId));
                batch.Update(_db.Collection("groups").Document(groupId), new Dictionary<string, object>
                {
                    { "MemberCount", Math.Max(0, group.MemberCount - 1) }
                });

                await batch.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error removing member: {ex.Message}");
                return false;
            }
        }

        // ─── DELETE GROUP ────────────────────────────────────────────────────────────
        public async Task<bool> DeleteGroupAsync(string groupId)
        {
            var (currentUserId, _) = await _currentUser.GetUserAsync();
            if (string.IsNullOrEmpty(currentUserId))
                currentUserId = _auth.UserId;

            if (string.IsNullOrEmpty(currentUserId)) return false;

            try
            {
                var groupDoc = await _db.Collection("groups").Document(groupId).GetSnapshotAsync();
                if (!groupDoc.Exists) return false;

                var group = groupDoc.ConvertTo<Group>();
                if (group.OwnerId != currentUserId) return false;

                var membersSnapshot = await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .GetSnapshotAsync();

                var batch = _db.StartBatch();
                foreach (var doc in membersSnapshot.Documents)
                {
                    batch.Delete(doc.Reference);
                }

                batch.Delete(_db.Collection("groups").Document(groupId));
                await batch.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting group: {ex.Message}");
                return false;
            }
        }

        // ─── GET GROUP MEMBER IDS ────────────────────────────────────────────────────
        public async Task<List<string>> GetGroupMemberIdsAsync(string groupId)
        {
            try
            {
                Console.WriteLine($"Getting member IDs for group: {groupId}");

                var membersSnapshot = await _db.Collection("groups")
                    .Document(groupId)
                    .Collection("members")
                    .GetSnapshotAsync();

                var memberIds = membersSnapshot.Documents.Select(d => d.Id).ToList();
                Console.WriteLine($"  └─ Found {memberIds.Count} member(s)");

                return memberIds;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting group member IDs: {ex.Message}");
                return new List<string>();
            }
        }
    }
}
