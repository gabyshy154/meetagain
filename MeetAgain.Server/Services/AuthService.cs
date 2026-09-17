using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using MeetAgain.Server.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using System;

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

        // -----------------------------
        // New: exposes last Firebase error
        // -----------------------------
        public string? LastFirebaseError { get; private set; }

public AuthService(FirestoreService fs, string firebaseApiKey)
{
    _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    _apiKey = firebaseApiKey ?? throw new ArgumentNullException(nameof(firebaseApiKey));
}
// ------------------------------------------------------
// REGISTER
// ------------------------------------------------------
public async Task<bool> SignUpAsync(string email, string password, string displayName)
{
    UserRecord? fbUser = null;
    try
    {
        // Create Firebase Auth user
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
        Console.WriteLine("Registration failed: " + LastFirebaseError);
        return false;
    }
    catch (Exception ex)
    {
        LastFirebaseError = "Firebase registration failed: " + ex.Message;
        Console.WriteLine(LastFirebaseError);
        return false;
    }

    // Create Firestore user document
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
        // Rollback Firebase user if Firestore write fails
        try
        {
            await FirebaseAuth.DefaultInstance.DeleteUserAsync(fbUser.Uid);
        }
        catch { /* ignore rollback failure */ }

        LastFirebaseError = "Failed to write user to Firestore: " + ex.Message;
        Console.WriteLine(LastFirebaseError);
        return false;
    }

    CurrentUser = user;
    LastFirebaseError = null;
    Console.WriteLine($"Registration successful: {email}");
    return true;
}


        // ------------------------------------------------------
        // LOGIN VIA FIREBASE REST API
        // ------------------------------------------------------
        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_apiKey}";

                var payload = new
                {
                    email = email,
                    password = password,
                    returnSecureToken = true
                };

                var response = await _http.PostAsJsonAsync(url, payload);
                var rawResponse = await response.Content.ReadAsStringAsync();

                Console.WriteLine("Firebase Login Response: " + rawResponse);

                if (!response.IsSuccessStatusCode)
                {
                    LastFirebaseError = rawResponse;
                    Console.WriteLine($"Login failed! Status code: {response.StatusCode}");
                    return false;
                }

                var json = JsonDocument.Parse(rawResponse).RootElement;

                string idToken = json.TryGetProperty("idToken", out var idTokenProp)
                    ? idTokenProp.GetString() ?? ""
                    : "";

                string localId = json.TryGetProperty("localId", out var localIdProp)
                    ? localIdProp.GetString() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(localId))
                {
                    LastFirebaseError = "idToken or localId missing from Firebase response.";
                    Console.WriteLine(LastFirebaseError);
                    return false;
                }

                var user = await _fs.GetUserAsync(localId);
                if (user == null)
                {
                    LastFirebaseError = "User not found in Firestore.";
                    Console.WriteLine(LastFirebaseError);
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
    Console.WriteLine("LogoutAsync CALLED");

    CurrentUser = null;
    if (AuthStateProvider != null)
        await AuthStateProvider.SetTokenAsync(null);

    LastFirebaseError = null;
    Console.WriteLine("User logged out.");
}





        // ------------------------------------------------------
        // REQUIRED BY PAGES
        // ------------------------------------------------------
        public Task<AppUser?> GetCurrentUserAsync()
        {
            return Task.FromResult(CurrentUser);
        }
    }
}
