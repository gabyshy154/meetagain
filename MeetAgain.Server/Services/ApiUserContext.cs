using System.Security.Claims;
using FirebaseAdmin.Auth;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Stateless helper to resolve the calling user inside API controllers.
    /// Priority: HttpContext claims -&gt; Bearer Firebase ID token -&gt; X-User-Id header -&gt; ?userId query.
    /// This avoids depending on the Blazor-circuit AuthService.CurrentUser / ProtectedSessionStorage.
    /// </summary>
    public static class ApiUserContext
    {
        public static string? GetUserIdFromClaims(HttpContext http)
        {
            var uid = http.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? http.User?.FindFirst("uid")?.Value
                ?? http.User?.FindFirst("user_id")?.Value;
            return string.IsNullOrWhiteSpace(uid) ? null : uid;
        }

        public static async Task<(string? Uid, string? Email)> ResolveAsync(
            HttpContext http,
            string? userIdQuery = null,
            string? userIdBody = null)
        {
            // 1. Claims already populated (if auth middleware ever added)
            var claimed = GetUserIdFromClaims(http);
            if (!string.IsNullOrWhiteSpace(claimed))
            {
                var email = http.User?.FindFirst(ClaimTypes.Email)?.Value;
                return (claimed, email);
            }

            // 2. Authorization: Bearer <Firebase ID token>
            if (http.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var bearer = authHeader.ToString();
                if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = bearer["Bearer ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        try
                        {
                            var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);
                            decoded.Claims.TryGetValue("email", out var emailObj);
                            return (decoded.Uid, emailObj?.ToString());
                        }
                        catch
                        {
                            // fall through to header/query fallback (dev convenience)
                        }
                    }
                }
            }

            // 3. X-User-Id header (simple stateless auth for dev/mobile without full Bearer flow)
            if (http.Request.Headers.TryGetValue("X-User-Id", out var headerUid))
            {
                var h = headerUid.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(h)) return (h, null);
            }

            // 4. Explicit userId from query string or body
            if (!string.IsNullOrWhiteSpace(userIdBody)) return (userIdBody.Trim(), null);
            if (!string.IsNullOrWhiteSpace(userIdQuery)) return (userIdQuery.Trim(), null);
            if (http.Request.Query.TryGetValue("userId", out var q))
            {
                var qv = q.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(qv)) return (qv, null);
            }

            return (null, null);
        }

        public static async Task<string?> RequireUserIdAsync(
            HttpContext http,
            string? userIdQuery = null,
            string? userIdBody = null)
        {
            var (uid, _) = await ResolveAsync(http, userIdQuery, userIdBody);
            return string.IsNullOrWhiteSpace(uid) ? null : uid;
        }
    }
}
