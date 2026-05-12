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
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _apiKey = firebaseApiKey ?? throw new ArgumentNullException(nameof(firebaseApiKey));
        }

        // ------------------------------------------------------
        // RESTORE SESSION (call this on every page load)
        // ------------------------------------------------------
        public async Task TryRestoreSessionAsync()
        {
            if (CurrentUser != null) return;
            if (AuthStateProvider == null) return;

            try
            {
                var authState = await AuthStateProvider.GetAuthenticationStateAsync();
                var user = authState.User;

                if (!(user.Identity?.IsAuthenticated ?? false)) return;

                var uid = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(uid)) return;

                CurrentUser = await _fs.GetUserAsync(uid);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TryRestoreSessionAsync failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------
        // REGISTER
        // ------------------------------------------------------
        public async Task<bool> SignUpAsync(string email, string password, string displayName)
        {
            UserRecord? fbUser = null;
            try
            {
                fbUser = await FirebaseAuth.DefaultInstance.CreateUserAsync(new UserRecordArgs
                {
                    Email = email,
                    Password = password,
                    DisplayName = displayName
                });
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
            {
                LastFirebaseError = "Email already exists.";
                return false;
            }
            catch (Exception ex)
            {
                LastFirebaseError = "Firebase registration failed: " + ex.Message;
                return false;
            }

            var user = new AppUser
            {
                Uid = fbUser.Uid,
                Email = fbUser.Email ?? email,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };

            try
            {
                await _fs.CreateOrUpdateUserAsync(user);
            }
            catch (Exception ex)
            {
                try { await FirebaseAuth.DefaultInstance.DeleteUserAsync(fbUser.Uid); } catch { }
                LastFirebaseError = "Failed to write user to Firestore: " + ex.Message;
                return false;
            }

            CurrentUser = user;
            LastFirebaseError = null;
            Console.WriteLine($"Registration successful: {email}");
            return true;
        }

        // ------------------------------------------------------
        // LOGIN
        // ------------------------------------------------------
        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_apiKey}";
                var payload = new { email, password, returnSecureToken = true };

                Console.WriteLine($">>> Before Firebase login POST: {DateTime.Now:HH:mm:ss}");
                var response = await _http.PostAsJsonAsync(url, payload);
                Console.WriteLine($">>> After Firebase login POST: {DateTime.Now:HH:mm:ss}");
                var rawResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var firebaseError = rawResponse ?? "";
                    
                    LastFirebaseError = firebaseError.Contains("INVALID_LOGIN_CREDENTIALS") ||
                                        firebaseError.Contains("INVALID_PASSWORD") ||
                                        firebaseError.Contains("EMAIL_NOT_FOUND")
                        ? "Invalid email or password. Please try again."
                        : "An error occurred during login. Please try again.";
                    
                    return false;
                }

                var json = JsonDocument.Parse(rawResponse).RootElement;

                string idToken = json.TryGetProperty("idToken", out var t) ? t.GetString() ?? "" : "";
                string localId = json.TryGetProperty("localId", out var l) ? l.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(localId))
                {
                    LastFirebaseError = "idToken or localId missing from Firebase response.";
                    return false;
                }

                var user = await _fs.GetUserAsync(localId);
                if (user == null)
                {
                    LastFirebaseError = "User not found in Firestore.";
                    return false;
                }

                CurrentUser = user;

                if (AuthStateProvider != null)
                    await AuthStateProvider.SetTokenAsync(idToken);

                LastFirebaseError = null;
                Console.WriteLine($"Login successful: {email}");
                return true;
            }
            catch (Exception ex)
            {
                LastFirebaseError = ex.Message;
                Console.WriteLine("Login Exception: " + ex);
                return false;
            }
        }

        // ------------------------------------------------------
        // LOGOUT
        // ------------------------------------------------------
        public async Task LogoutAsync()
        {
            CurrentUser = null;
            if (AuthStateProvider != null)
                await AuthStateProvider.SetTokenAsync(null);

            LastFirebaseError = null;
            Console.WriteLine("User logged out.");
        }

        // ------------------------------------------------------
        // GET CURRENT USER
        // ------------------------------------------------------
        public Task<AppUser?> GetCurrentUserAsync()
        {
            return Task.FromResult(CurrentUser);
        }
    }
}