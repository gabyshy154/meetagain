using MeetAgain.Server.Models;
using MeetAgain.Shared.Contracts;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeetAgain.Server.Services
{
    /// <summary>
    /// Sign-up, login and password reset for the web app (Stage D).
    /// Talks to Firebase's public REST endpoints (needs only the public web API key)
    /// and to MeetAgain.Api for the user's profile. No Admin SDK, no service-account key.
    /// </summary>
    public class AuthService
    {
        private readonly ApiClient _api;
        private readonly HttpClient _http = new();
        private readonly string _apiKey;

        public AppUser? CurrentUser { get; private set; }
        public string? UserId => CurrentUser?.Uid;

        public CustomAuthStateProvider? AuthStateProvider { get; set; }
        public string? LastFirebaseError { get; private set; }

        public AuthService(ApiClient api, string firebaseApiKey)
        {
            _api    = api             ?? throw new ArgumentNullException(nameof(api));
            _apiKey = firebaseApiKey  ?? throw new ArgumentNullException(nameof(firebaseApiKey));
        }

        // ── REGISTER ──────────────────────────────────────────────────────────
        public async Task<bool> SignUpAsync(string email, string password, string displayName)
        {
            string idToken;
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";
                var response = await _http.PostAsJsonAsync(url, new { email, password, returnSecureToken = true });
                var raw = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LastFirebaseError = ParseFirebaseError(raw);
                    Console.WriteLine("Registration failed: " + LastFirebaseError);
                    return false;
                }

                var json = JsonDocument.Parse(raw).RootElement;
                idToken = json.TryGetProperty("idToken", out var t) ? t.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(idToken))
                {
                    LastFirebaseError = "Unexpected response from Firebase. Please try again.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                LastFirebaseError = "A network error occurred. Please try again.";
                Console.WriteLine("Registration exception: " + ex.Message);
                return false;
            }

            // Best effort: store the display name on the Firebase account too
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:update?key={_apiKey}";
                await _http.PostAsJsonAsync(url, new { idToken, displayName, returnSecureToken = false });
            }
            catch { /* the profile in Firestore is what the app actually uses */ }

            // Create the profile through the API
            var result = await _api.SendWithTokenAsync<AppUser>(idToken, HttpMethod.Post, "api/users/me",
                new CreateProfileRequest { DisplayName = displayName, Email = email });

            if (!result.Ok || result.Value == null)
            {
                // Roll back the Firebase account so the email isn't permanently locked
                try
                {
                    var url = $"https://identitytoolkit.googleapis.com/v1/accounts:delete?key={_apiKey}";
                    await _http.PostAsJsonAsync(url, new { idToken });
                }
                catch { /* ignore rollback failure */ }

                LastFirebaseError = "Could not create your profile. Please try again.";
                Console.WriteLine($"Registration failed: profile creation returned status {result.Status}");
                return false;
            }

            CurrentUser       = result.Value;
            LastFirebaseError = null;
            Console.WriteLine($"Registration successful: {email}");
            return true;
        }

        // ── LOGIN ─────────────────────────────────────────────────────────────
        /// <param name="rememberMe">
        ///   true  → token stored in localStorage  (survives browser restart) <br/>
        ///   false → token stored in sessionStorage (clears when tab closes)
        /// </param>
        public async Task<bool> LoginAsync(string email, string password, bool rememberMe = false)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_apiKey}";
                var payload = new { email, password, returnSecureToken = true };

                var response    = await _http.PostAsJsonAsync(url, payload);
                var rawResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LastFirebaseError = ParseFirebaseError(rawResponse);
                    Console.WriteLine($"Login failed: {LastFirebaseError}");
                    return false;
                }

                var json = JsonDocument.Parse(rawResponse).RootElement;
                var idToken = json.TryGetProperty("idToken", out var idProp) ? idProp.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(idToken))
                {
                    LastFirebaseError = "Unexpected response from Firebase. Please try again.";
                    Console.WriteLine(LastFirebaseError);
                    return false;
                }

                // Load the profile from the API using the fresh token
                var profile = await _api.SendWithTokenAsync<AppUser>(idToken, HttpMethod.Get, "api/users/me");
                if (profile.Status == 404)
                {
                    LastFirebaseError = "User account not found. Please register first.";
                    Console.WriteLine(LastFirebaseError);
                    return false;
                }
                if (!profile.Ok || profile.Value == null)
                {
                    LastFirebaseError = "Could not reach the server. Please try again.";
                    Console.WriteLine($"Login failed: profile request returned status {profile.Status}");
                    return false;
                }

                CurrentUser = profile.Value;

                if (AuthStateProvider != null)
                    await AuthStateProvider.SetTokenAsync(idToken, rememberMe);

                LastFirebaseError = null;
                Console.WriteLine($"Login successful: {email} (rememberMe={rememberMe})");
                return true;
            }
            catch (Exception ex)
            {
                LastFirebaseError = "A network error occurred. Please try again.";
                Console.WriteLine("Login Exception: " + ex);
                return false;
            }
        }

        // ── FORGOT PASSWORD ───────────────────────────────────────────────────
        public async Task<bool> SendPasswordResetEmailAsync(string email)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={_apiKey}";
                var payload = new { requestType = "PASSWORD_RESET", email = email };

                var response = await _http.PostAsJsonAsync(url, payload);
                var body     = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    LastFirebaseError = null;
                    Console.WriteLine($"✅ Password reset email sent to {email}");
                    return true;
                }

                LastFirebaseError = ParseFirebaseError(body);
                Console.WriteLine($"❌ Password reset failed: {LastFirebaseError}");
                return false;
            }
            catch (Exception ex)
            {
                LastFirebaseError = "A network error occurred. Please try again.";
                Console.WriteLine($"❌ Password reset exception: {ex.Message}");
                return false;
            }
        }

        // ── LOGOUT ────────────────────────────────────────────────────────────
        public async Task LogoutAsync()
        {
            Console.WriteLine("LogoutAsync called");
            CurrentUser = null;

            if (AuthStateProvider != null)
                await AuthStateProvider.SetTokenAsync(null); // clears both stores

            LastFirebaseError = null;
            Console.WriteLine("User logged out.");
        }

        // ── HELPERS ───────────────────────────────────────────────────────────
        public Task<AppUser?> GetCurrentUserAsync() => Task.FromResult(CurrentUser);

        /// <summary>Parses a Firebase REST API error body into a user-friendly string.</summary>
        private static string ParseFirebaseError(string body)
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                {
                    return msg.GetString() switch
                    {
                        "EMAIL_NOT_FOUND"             => "No account found with that email address.",
                        "INVALID_EMAIL"               => "The email address is not valid.",
                        "INVALID_PASSWORD"            => "Incorrect password. Please try again.",
                        "USER_DISABLED"               => "This account has been disabled.",
                        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many failed attempts. Please try again later.",
                        "INVALID_LOGIN_CREDENTIALS"   => "Incorrect email or password.",
                        "EMAIL_EXISTS"                => "Email already exists.",
                        "OPERATION_NOT_ALLOWED"       => "Sign-up is currently disabled.",
                        var m when m != null && m.StartsWith("WEAK_PASSWORD")
                                                      => "Password should be at least 6 characters.",
                        var other                     => $"Error: {other}"
                    };
                }
            }
            catch { /* fall through */ }

            return "An unexpected error occurred. Please try again.";
        }
    }
}
