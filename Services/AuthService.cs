using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeetAgain.Server.Services
{
    public class AuthService
    {
        private readonly FirestoreService _fs;
        private readonly HttpClient _http = new();
        private readonly string _apiKey;

        public AppUser? CurrentUser { get; private set; }
        public string? UserId => CurrentUser?.Uid;

        public CustomAuthStateProvider? AuthStateProvider { get; set; }
        public string? LastFirebaseError { get; private set; }

        public AuthService(FirestoreService fs, string firebaseApiKey)
        {
            _fs      = fs              ?? throw new ArgumentNullException(nameof(fs));
            _apiKey  = firebaseApiKey  ?? throw new ArgumentNullException(nameof(firebaseApiKey));
        }

        // ── REGISTER ──────────────────────────────────────────────────────────
        public async Task<bool> SignUpAsync(string email, string password, string displayName)
        {
            UserRecord? fbUser = null;
            try
            {
                fbUser = await FirebaseAuth.DefaultInstance.CreateUserAsync(new UserRecordArgs
                {
                    Email       = email,
                    Password    = password,
                    DisplayName = displayName
                });
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
            {
                LastFirebaseError = "Email already exists.";
                Console.WriteLine("Registration failed: " + LastFirebaseError);
                return false;
            }
            catch (Exception ex)
            {
                LastFirebaseError = "Firebase registration failed: " + ex.Message;
                Console.WriteLine(LastFirebaseError);
                return false;
            }

            var user = new AppUser
            {
                Uid         = fbUser.Uid,
                Email       = fbUser.Email ?? email,
                DisplayName = displayName,
                CreatedAt   = DateTime.UtcNow.ToString("o")
            };

            try
            {
                await _fs.CreateOrUpdateUserAsync(user);
            }
            catch (Exception ex)
            {
                // Roll back the Firebase Auth record so the email isn't permanently locked
                try { await FirebaseAuth.DefaultInstance.DeleteUserAsync(fbUser.Uid); }
                catch { /* ignore rollback failure */ }

                LastFirebaseError = "Failed to write user to Firestore: " + ex.Message;
                Console.WriteLine(LastFirebaseError);
                return false;
            }

            CurrentUser       = user;
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

                var payload = new
                {
                    email             = email,
                    password          = password,
                    returnSecureToken = true
                };

                var response    = await _http.PostAsJsonAsync(url, payload);
                var rawResponse = await response.Content.ReadAsStringAsync();

                Console.WriteLine("Firebase Login Response: " + rawResponse);

                if (!response.IsSuccessStatusCode)
                {
                    LastFirebaseError = ParseFirebaseError(rawResponse);
                    Console.WriteLine($"Login failed: {LastFirebaseError}");
                    return false;
                }

                var json = JsonDocument.Parse(rawResponse).RootElement;

                var idToken = json.TryGetProperty("idToken",  out var idProp)  ? idProp.GetString()  ?? "" : "";
                var localId = json.TryGetProperty("localId",  out var lidProp) ? lidProp.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(localId))
                {
                    LastFirebaseError = "Unexpected response from Firebase. Please try again.";
                    Console.WriteLine(LastFirebaseError);
                    return false;
                }

                var user = await _fs.GetUserAsync(localId);
                if (user == null)
                {
                    LastFirebaseError = "User account not found. Please register first.";
                    Console.WriteLine(LastFirebaseError);
                    return false;
                }

                CurrentUser = user;

                // ✅ Thread rememberMe through to the state provider
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

                Console.WriteLine($"Password reset response: {body}");

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
                        var other                     => $"Error: {other}"
                    };
                }
            }
            catch { /* fall through */ }

            return "An unexpected error occurred. Please try again.";
        }
    }
}