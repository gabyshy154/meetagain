using System.Security.Claims;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MeetAgain.Server.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private const string TokenKey      = "authToken";
        private const string RememberMeKey = "rememberMe";

        // Session storage  → clears when the tab/browser closes
        private readonly ProtectedSessionStorage _session;
        // Local storage    → persists across browser restarts
        private readonly ProtectedLocalStorage   _local;

        private string? _token;

        public CustomAuthStateProvider(
            ProtectedSessionStorage session,
            ProtectedLocalStorage   local)
        {
            _session = session;
            _local   = local;
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

                // Validate with Firebase
                FirebaseToken decoded;
                try
                {
                    decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(_token);
                }
                catch (FirebaseAdmin.Auth.FirebaseAuthException ex)
                {
                    // Token expired or revoked — clean up both stores
                    Console.WriteLine($"AuthStateProvider: token invalid ({ex.AuthErrorCode}), clearing");
                    _token = null;
                    await TryDelete(_session, TokenKey);
                    await TryDelete(_local,   TokenKey);
                    await TryDelete(_local,   RememberMeKey);
                    return anonymous;
                }

                decoded.Claims.TryGetValue("email", out var emailObj);
                var email = emailObj?.ToString() ?? string.Empty;

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, decoded.Uid ?? string.Empty),
                    new Claim(ClaimTypes.Email,          email),
                    // Keep "user_id" claim so CurrentUserAccessor keeps working
                    new Claim("user_id",                 decoded.Uid ?? string.Empty)
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

        // ── Helper: swallow errors on Delete (key may not exist) ─────────────
        private static async Task TryDelete<T>(T storage, string key)
            where T : ProtectedBrowserStorage
        {
            try   { await storage.DeleteAsync(key); }
            catch { /* key didn't exist — safe to ignore */ }
        }
    }
}