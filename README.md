# MeetAgain – `split-api` branch

**Group 5 – Ctrl + Alt + Elite** (IT 3206N)
Neil Francis Gabisay · Humprey Manango · Jericho Andreou · Gabriel Sabang

MeetAgain helps friends find a time to meet. This branch separates the Blazor frontend from a dedicated backend API server.

> **Branch purpose:** the live site is a single ASP.NET Core Blazor Server app. On this branch the application is split into a web server (UI only) and an API server (all business logic and database access), so each can be deployed and scaled on its own and the database credentials live only on the backend.

---

## Current status

Last updated: November 10, 2026

| Area | Status |
|---|---|
| Original app (all 6 checkpoints) | Complete, live on AWS Elastic Beanstalk (`single-app-final` tag) |
| Web app calls the API for all data (Notifications, Availability, Friends, Groups, Meetups, profile/sign-up/login) | Done in code |
| Web app free of Firestore and the Firebase Admin SDK | Done. The `FirebaseAdmin` and `Google.Cloud.Firestore` packages, `FirestoreService` and `Diagnostic.razor` are removed |
| Solution builds | Passing (0 errors, 0 warnings) |
| API protection (anonymous requests) | Verified: `/health` returns 200; `/api/meetups`, `/api/users/me`, `/api/friends` and `/api/notifications/unread-count` return 401 without a token; an invalid token returns 401 `invalid_token` |
| API with a valid token | In progress (authenticated requests still to be confirmed) |
| Browser tests (register, login, edit meetup, dashboard) | In progress |
| Commit and push of the Stage D changes | Pending |
| API deployed as its own Elastic Beanstalk environment | Not started |
| Web environment pointed at the API (`Api__BaseUrl`) | Not started |
| Load balancer + HTTPS (AWS Certificate Manager) | Planned |
| AWS Secrets Manager for the Firebase service-account credentials | Planned |

The production site is unchanged: it still runs the old single app until the API is deployed and the web environment is switched over.

---

## Solution structure

```
MeetAgain-neil/
├── MeetAgain.Server   Blazor Server web app (UI only, no database access)
├── MeetAgain.Api      ASP.NET Core Web API (business logic, token verification, Firestore)
└── MeetAgain.Shared   Request/response models used by both projects
```

```
Browser
  │  SignalR (Blazor UI)
  ▼
MeetAgain.Server (web) ── HTTP/JSON + Bearer Firebase ID token ──▶ MeetAgain.Api
  │                                                                   │
  │ login / sign-up via Firebase REST (public API key)                │ verifies the token,
  ▼                                                                   │ enforces ownership
Firebase Authentication                                               ▼
                                                              Cloud Firestore
```

| Piece | Holds | Does not hold |
|---|---|---|
| Web (Blazor) | Pages, UI, the user's ID token, the public Firebase web API key | Firestore access, Admin SDK, service-account key |
| API | Service logic, token verification, ownership checks, Firestore credentials | The UI |
| Shared | Models | Logic |

The two servers communicate over plain HTTP with JSON (`HttpClient` via `IHttpClientFactory` and `System.Net.Http.Json`). The web app's `ApiClient` attaches `Authorization: Bearer <Firebase ID token>` to every call.

---

## Configuration

**Web (`MeetAgain.Server`)**

| Setting | Purpose |
|---|---|
| `Firebase:ApiKey` | Public Firebase web key (login and sign-up REST calls) |
| `Firebase:ProjectId` | Used to check the token's issuer and audience |
| `Api:BaseUrl` | Where MeetAgain.Api is hosted (on AWS: environment property `Api__BaseUrl`) |

The web app starts only if all three are set. It needs no service-account key.

**API (`MeetAgain.Api`)** needs the Firebase service-account credentials. Never commit them to the repository. On AWS they are supplied as Elastic Beanstalk environment properties (Secrets Manager is planned).

---

## Run locally

Use two terminals. Both programs stay running.

```bash
# Terminal 1: API
dotnet run --project MeetAgain.Api --urls http://localhost:5100

# Terminal 2: web app
dotnet run --project MeetAgain.Server
```

Local runs use the live Firestore project, so use clearly named test accounts and test data.

**Quick API check** (from a third terminal):

```bash
curl -i http://localhost:5100/health            # 200 {"status":"ok"}
curl -i http://localhost:5100/api/meetups       # 401 without a token
```

To call protected endpoints, sign in to Firebase (`accounts:signInWithPassword`) to get an ID token and send it as `Authorization: Bearer <token>`. Tokens expire after about an hour. Never paste tokens or passwords into chat, issues or commits.

---

## API endpoints

All endpoints require a valid Firebase ID token except `/health`.

### Program.cs (minimal APIs)

| Method | Route | Description |
|---|---|---|
| GET | `/health` | Health check (anonymous) |
| GET | `/api/me` | Get signed-in user info (authorized) |

### UsersController (`api/users`)

| Method | Route | Description |
|---|---|---|
| GET | `/api/users/me` | Get current user profile (404 if none exists) |
| POST | `/api/users/me` | Save/update current user profile (uid and email come from the token) |

### MeetupsController (`api/meetups`)

| Method | Route | Description |
|---|---|---|
| GET | `/api/meetups` | Get current user's meetups |
| POST | `/api/meetups` | Create meetup |
| GET | `/api/meetups/{id}` | Get meetup record (with permission check) |
| GET | `/api/meetups/{id}/detail` | Get meetup detail (creator or participants only) |
| POST | `/api/meetups/{id}/respond` | RSVP / respond to meetup |
| PUT | `/api/meetups/{id}` | Update meetup (creator only, checked against the stored record) |
| DELETE | `/api/meetups/{id}` | Delete meetup (creator only) |

### FriendsController (`api/friends`)

| Method | Route | Description |
|---|---|---|
| GET | `/api/friends` | Get current user's friends |
| DELETE | `/api/friends/{friendId}` | Remove friend |
| GET | `/api/friends/requests` | Get pending friend requests |
| POST | `/api/friends/requests` | Send friend request |
| POST | `/api/friends/requests/{fromUserId}/accept` | Accept friend request |
| POST | `/api/friends/requests/{fromUserId}/reject` | Reject friend request |
| GET | `/api/friends/search?email=` | Search user by email |

### GroupsController (`api/groups`)

| Method | Route | Description |
|---|---|---|
| GET | `/api/groups` | Get current user's groups |
| POST | `/api/groups` | Create group |
| GET | `/api/groups/{id}` | Get group detail |
| GET | `/api/groups/{id}/member-ids` | Get group member IDs |
| POST | `/api/groups/{id}/members` | Add members to group |
| DELETE | `/api/groups/{id}/members/{memberId}` | Remove member from group |
| DELETE | `/api/groups/{id}` | Delete group |

### AvailabilityController (`api/availability`)

| Method | Route | Description |
|---|---|---|
| GET | `/api/availability/{userId}` | Get user availability (permission-checked) |
| PUT | `/api/availability` | Save user availability |
| POST | `/api/availability/best-times` | Find best meetup times |

### NotificationsController (`api/notifications`)

| Method | Route | Description |
|---|---|---|
| GET | `/api/notifications` | Get current user's notifications |
| GET | `/api/notifications/unread-count` | Get unread notification count |
| POST | `/api/notifications/{id}/read` | Mark notification as read |
| POST | `/api/notifications/read-all` | Mark all notifications as read |
| DELETE | `/api/notifications/{id}` | Delete notification |

---

## Security notes

- The API is the security boundary. The web app cannot read or write Firestore, and the API decides what each signed-in user may do.
- User identity (uid, email) always comes from the verified token, never from the request body.
- Meetup update and delete check ownership against the stored meetup, not the client's copy. Meetup detail is limited to the creator and participants.
- The web server only checks a token's expiry, issuer and audience to build the UI's claims. It does not verify the signature. The API verifies the signature on every call.

**Known items to review**
- `POST /api/meetups` accepts any user ID as an invitee. It should be limited to the caller's friends.
- `POST /api/meetups/{id}/respond` accepts any text as an RSVP status. It should allow only `accepted`, `declined` and `maybe`.
- Blazor's protected browser storage uses ASP.NET Data Protection keys that live on the instance's disk by default, so users are signed out after a redeploy or when running more than one instance.
- If the web environment ever runs on more than one instance, the load balancer needs sticky sessions because Blazor Server keeps a SignalR connection to one instance.

---

## Deployment plan

1. Create a second Elastic Beanstalk environment for `MeetAgain.Api` with the Firebase credentials as environment properties.
2. Set `Api__BaseUrl`, `Firebase__ApiKey` and `Firebase__ProjectId` on the web environment.
3. Test the web environment against the deployed API, then switch over from the single app.
4. Add a load balancer with an AWS Certificate Manager certificate for HTTPS.
5. Move the Firebase service-account credentials into AWS Secrets Manager.

---

## Stack

.NET 9 · ASP.NET Core Blazor Server · ASP.NET Core Web API · Firebase Authentication · Cloud Firestore · AWS Elastic Beanstalk · Amazon CloudWatch
