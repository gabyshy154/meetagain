using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using MeetAgain.Server.Services;
using Microsoft.AspNetCore.Components.Authorization;


var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Load Firebase settings
// ------------------------------------------------------
var credentialsPath = builder.Configuration["Firebase:CredentialsFile"];
var projectId = builder.Configuration["Firebase:ProjectId"];
var apiKey = builder.Configuration["Firebase:ApiKey"];

if (string.IsNullOrWhiteSpace(credentialsPath))
    throw new Exception("Missing Firebase:CredentialsFile");
if (!Path.IsPathRooted(credentialsPath))
{
    credentialsPath = Path.Combine(builder.Environment.ContentRootPath, credentialsPath);
}
if (string.IsNullOrWhiteSpace(projectId))
    throw new Exception("Missing Firebase:ProjectId");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new Exception("Missing Firebase:ApiKey");

// ------------------------------------------------------
// Initialize Firebase Admin SDK
// ------------------------------------------------------
var firebaseCredential = await CredentialFactory.FromFileAsync<ServiceAccountCredential>(credentialsPath, CancellationToken.None);
GoogleCredential googleCred = firebaseCredential
    .ToGoogleCredential()
    .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = googleCred
    });
}

// ------------------------------------------------------
// Firestore
// ------------------------------------------------------
var firestoreDb = new FirestoreDbBuilder
{
    ProjectId = projectId,
    Credential = googleCred
}.Build();

builder.Services.AddSingleton(firestoreDb);

// ------------------------------------------------------
// Blazor & Authentication
// ------------------------------------------------------
builder.Services.AddRazorPages();


// Add circuit options to maintain state
builder.Services.AddServerSideBlazor().AddCircuitOptions(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CustomAuthStateProvider>());

// ------------------------------------------------------
// App Services - IMPORTANT: Make services per-circuit
// ------------------------------------------------------
builder.Services.AddScoped<FirestoreService>();

// AuthService should be Scoped but maintain state via AuthStateProvider
builder.Services.AddScoped<AuthService>(sp =>
{
    var fs = sp.GetRequiredService<FirestoreService>();
    var authStateProvider = sp.GetRequiredService<AuthenticationStateProvider>() as CustomAuthStateProvider;

    var svc = new AuthService(fs, apiKey);
    svc.AuthStateProvider = authStateProvider;
    fs.AuthStateProvider = authStateProvider;

    return svc;
});

builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<MeetupService>();

// ------------------------------------------------------
// Build app
// ------------------------------------------------------
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();