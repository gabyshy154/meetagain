using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Keeps the signed-in user's Firebase ID token in protected browser storage and
    /// turns it into the user's claims for the UI.
    ///
    /// Stage D: the web server no longer holds the Firebase Admin key, so it no longer
    /// verifies token signatures. It checks expiry, issuer and audience, and reads the claims.
    /// The token lives in ASP.NET Data Protection storage, which the browser cannot forge, and
    /// MeetAgain.Api verifies the real signature on every call, so all data access is still enforced there.
    /// </summary>
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private const string TokenKey      = "authToken";
        private const string RememberMeKey = "rememberMe";

        // Session storage  → clears when the tab/browser closes
        private readonly ProtectedSessionStorage _session;
        // Local storage    → persists across browser restarts
        private readonly ProtectedLocalStorage   _local;
        private readonly string _projectId;

        private string? _token;

        /// <summary>The current Firebase ID token. Used by ApiClient to call the API.</summary>
        public string? Token => _token;

        public CustomAuthStateProvider(
            ProtectedSessionStorage session,
            ProtectedLocalStorage   local,
            IConfiguration          config)
        {
            _session   = session;
            _local     = local;
            _projectId = config["Firebase:ProjectId"] ?? "";
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Call this after a successful login.
        /// Pass rememberMe = true  → token goes into localStorage  (survives browser restart)
        /// Pass rememberMe = false → token goes into sessionStorage (clears on tab close)
        /// </summary>
        public async Task SetTokenAsync(string? token, bool rememberMe = false)
        {
            _token = token;

            if (string.IsNullOrWhiteSpace(token))
            {
                // Logout — wipe both stores so no stale token lingers
                await TryDelete(_session, TokenKey);
                await TryDelete(_local,   TokenKey);
                await TryDelete(_local,   RememberMeKey);
            }
            else if (rememberMe)
            {
                // Persist across browser restarts
                await _local.SetAsync(TokenKey,      token);
                await _local.SetAsync(RememberMeKey, true);
                // Clear any leftover session token so we don't have duplicates
                await TryDelete(_session, TokenKey);
            }
            else
            {
                // Session-only
                await _session.SetAsync(TokenKey, token);
                // Clear any leftover local token (e.g. user had previously used Remember Me)
                await TryDelete(_local, TokenKey);
                await TryDelete(_local, RememberMeKey);
            }

            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        // ── Core override ─────────────────────────────────────────────────────
        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var anonymous = new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity()));

            try
            {
                // 1. Use the in-memory token if we already have it
                if (string.IsNullOrWhiteSpace(_token))
                {
                    // 2. Try localStorage first (Remember Me path)
                    var localResult = await _local.GetAsync<string>(TokenKey);
                    if (localResult.Success && !string.IsNullOrWhiteSpace(localResult.Value))
                    {
                        _token = localResult.Value;
                        Console.WriteLine("AuthStateProvider: token loaded from localStorage (Remember Me)");
                    }
                    else
                    {
                        // 3. Fall back to sessionStorage
                        var sessionResult = await _session.GetAsync<string>(TokenKey);
                        if (sessionResult.Success && !string.IsNullOrWhiteSpace(sessionResult.Value))
                        {
                            _token = sessionResult.Value;
                            Console.WriteLine("AuthStateProvider: token loaded from sessionStorage");
                        }
                    }
                }

                Console.WriteLine($"AuthStateProvider: token is " +
                    $"{(string.IsNullOrWhiteSpace(_token) ? "null/empty" : "present")}");

                if (string.IsNullOrWhiteSpace(_token))
                    return anonymous;

                // Check expiry / issuer / audience and read the claims
                if (!TryReadToken(_token, out var uid, out var email))
                {
                    // Token expired or not ours — clean up both stores
                    Console.WriteLine("AuthStateProvider: token invalid or expired, clearing");
                    _token = null;
                    await TryDelete(_session, TokenKey);
                    await TryDelete(_local,   TokenKey);
                    await TryDelete(_local,   RememberMeKey);
                    return anonymous;
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, uid),
                    new Claim(ClaimTypes.Email,          email),
                    // Keep "user_id" claim so CurrentUserAccessor keeps working
                    new Claim("user_id",                 uid)
                };

                var identity  = new ClaimsIdentity(claims, "firebase");
                var principal = new ClaimsPrincipal(identity);
                return new AuthenticationState(principal);
            }
            catch (Exception ex)
            {
                // Any unexpected error → treat as anonymous, wipe stores
                Console.WriteLine($"AuthStateProvider unexpected error: {ex.Message}");
                _token = null;
                await TryDelete(_session, TokenKey);
                await TryDelete(_local,   TokenKey);
                await TryDelete(_local,   RememberMeKey);
                return anonymous;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Reads uid/email from a Firebase ID token and checks expiry, issuer and audience.</summary>
        private bool TryReadToken(string token, out string uid, out string email)
        {
            uid = "";
            email = "";
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return false;

                using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
                var root = doc.RootElement;

                if (!root.TryGetProperty("exp", out var expEl) || !expEl.TryGetInt64(out var exp)) return false;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp) return false;

                if (!string.IsNullOrEmpty(_projectId))
                {
                    if (!root.TryGetProperty("aud", out var aud) || aud.GetString() != _projectId) return false;
                    if (!root.TryGetProperty("iss", out var iss) ||
                        iss.GetString() != $"https://securetoken.google.com/{_projectId}") return false;
                }

                if (root.TryGetProperty("user_id", out var u))       uid = u.GetString() ?? "";
                else if (root.TryGetProperty("sub", out var s))      uid = s.GetString() ?? "";
                if (string.IsNullOrEmpty(uid)) return false;

                if (root.TryGetProperty("email", out var e)) email = e.GetString() ?? "";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "=";  break;
            }
            return Convert.FromBase64String(s);
        }

        // ── Helper: swallow errors on Delete (key may not exist) ─────────────
        private static async Task TryDelete<T>(T storage, string key)
            where T : ProtectedBrowserStorage
        {
            try   { await storage.DeleteAsync(key); }
            catch { /* key didn't exist — safe to ignore */ }
        }
    }
}
