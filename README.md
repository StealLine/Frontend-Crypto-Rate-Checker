# Frontend & CryptoVault App — Documentation

---

### Structure
<img width="298" height="465" alt="image" src="https://github.com/user-attachments/assets/fc95ffea-5ddf-45b5-b92e-f023e38dd4cb" />
<img width="235" height="103" alt="image" src="https://github.com/user-attachments/assets/46dcacac-a1b0-41ac-9299-13282e595fba" />

## 1. Frontend (nginx)


### `dockerfile_nginx`
```dockerfile
FROM nginx:alpine
 
COPY Crypto_Rate_Checker/webstatic/nginx.conf /etc/nginx/conf.d/default.conf
COPY Crypto_Rate_Checker/webstatic/www/ /usr/share/nginx/html/
 
EXPOSE 80
```
Copies config and static files into the image. No build step, no dependencies.

> `www/` folder must exist at the root of the Docker build context, otherwise build fails with `/www: not found`.

### `nginx.conf`
```nginx
server {
    listen 80;
 
    location / {
        root /usr/share/nginx/html;
        index login.html;
        try_files $uri $uri/ =404;
    }
 
    location /api/ {
        proxy_pass http://cryptovault:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
 
```
Serves files from `/usr/share/nginx/html`.Proxies all req to http://cryptovault:8080; where cryptovault host of the container where this backend part of this app is working

### Pages and what they do

| Page | Description |
|------|-------------|
| `login.html` | Sign in form. Redirects to dashboard if already logged in. |
| `register.html` | Create account form. Redirects to dashboard if already logged in. |
| `dashboard.html` | Shows live crypto prices. Redirects to login if not authenticated. |

All pages make `fetch()` calls to `/api/Auth/...`. The nginx sends them to the http://cryptovault:8080; (in this case, your host name can be different). Pages never know the backend address directly.


## 2. CryptoVault App (ASP.NET Core)

### What it does
Handles user authentication. Does not connect to PostgreSQL directly — all SQL is sent to PROXY_TODB which executes it in the correct database.

### Structure
```
CryptoVault/
  Controllers/
    AuthController.cs       # HTTP endpoints
  Services/
    AuthService.cs          # business logic
    InitDbHostedService.cs  # runs on startup
  Models/                 # request/response models
    AppState.cs               # holds db hash in memory
    Program.cs                # app setup

  appsettings.json          # configuration
  db_hash.txt               # created automatically on first run
```

### `Program.cs`
```csharp
builder.Services.AddHttpClient("ProxyClient", client =>
{
    string baseUrl = builder.Configuration["ProxyApi:BaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<InitDbHostedService>();
```
Registers the HTTP client used to talk to [PROXY_TODB](https://github.com/StealLine/PROXY_DB_APP) and the startup service. Base URL comes from config or environment variable.
where PROXYAPI__BASEUR is env variable which points to hostname where [PROXY_TODB](https://github.com/StealLine/PROXY_DB_APP) works. Ex PROXYAPI__BASEURL: http://proxy_dep:8080

### `appsettings.json`
```json
{
  "ProxyApi": {
    "BaseUrl": "http://localhost:5000"
  }
}
```
Default address of PROXY_TODB. Overridden in Docker by environment variable `PROXYAPI__BASEURL`.

### `AppState.cs`
```csharp
public static class AppState
{
    public static string DbHash { get; set; } = string.Empty;
    public static bool IsReady { get; set; } = false;
}
```
Simple static class that holds the database hash in memory while the app is running. Gets populated once on startup by `InitDbHostedService`.

### `InitDbHostedService.cs`
Runs once when the app starts. Responsible for finding or creating the database.

**Flow:**
```
Start
  │
  ├── db_hash.txt exists? ──yes──► read hash from file
  │
  └── no ──► call GET /Home/CreateDB on PROXY_TODB
                  └── save returned hash to db_hash.txt
  │
  └── run CREATE TABLE IF NOT EXISTS for users and sessions
  │
  └── set AppState.IsReady = true
```

**`db_hash.txt`** — plain text file containing the hash of the database this instance uses. Created automatically on first run. If deleted or lost, the app creates a brand new empty database on next startup (all users and sessions are lost from the app's perspective — the old database still exists in PostgreSQL but the app no longer knows about it). Creating this hashes is handled by [PROXY_TODB](https://github.com/StealLine/PROXY_DB_APP)

When running multiple instances, this file must be on a shared volume so all instances use the same database.

### `AuthController.cs`
Exposes the HTTP endpoints consumed by the frontend pages.

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/Auth/Register` | Creates a new user |
| `POST` | `/api/Auth/Login` | Validates credentials, sets `session_token` cookie |
| `POST` | `/api/Auth/Logout` | Deletes session, clears cookie |
| `GET` | `/api/Auth/Me` | Returns `{ authenticated, username }` for the current session |

Session cookie is `HttpOnly` — JavaScript on the frontend cannot read it, only send it automatically with every request.

### `AuthService.cs`
Contains all the actual logic. For every operation it builds a SQL string and sends it to PROXY_TODB via `POST /Home/ExecuteScript` with the hash from `AppState.DbHash`.

| Method | What it does |
|--------|--------------|
| `RegisterAsync` | Checks for duplicate email/username, inserts new user |
| `LoginAsync` | Verifies password hash, creates session with 1 hour expiry |
| `ValidateSessionAsync` | Checks if token exists and is not expired |
| `LogoutAsync` | Deletes session by token |

Passwords are hashed with SHA-256 before storing or comparing. Sessions are cleaned up on each login (expired ones are deleted).

### Environment variables

| Variable | Description | Example |
|----------|-------------|---------|
| `PROXYAPI__BASEURL` | Address of PROXY_TODB | `http://proxy-todb:5000` |
| `ASPNETCORE_URLS` | Port the app listens on | `http://+:5133` |
