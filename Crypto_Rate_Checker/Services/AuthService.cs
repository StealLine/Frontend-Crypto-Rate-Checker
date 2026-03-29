using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UserAuthApp;
using UserAuthApp.Models;

namespace UserAuthApp.Services;

public class AuthService
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger<AuthService> _logger;

	public AuthService(IHttpClientFactory factory, ILogger<AuthService> logger)
	{
		_httpClientFactory = factory;
		_logger = logger;
	}

	public async Task<(bool ok, string message)> RegisterAsync(RegisterRequest req)
	{
		if (string.IsNullOrWhiteSpace(req.Username) ||
			string.IsNullOrWhiteSpace(req.Email) ||
			string.IsNullOrWhiteSpace(req.Password))
			return (false, "All fields are required.");

		string hash = HashPassword(req.Password);

		string sql = $@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM users WHERE email = '{Esc(req.Email)}') THEN
                    RAISE EXCEPTION 'EMAIL_EXISTS';
                END IF;
                IF EXISTS (SELECT 1 FROM users WHERE username = '{Esc(req.Username)}') THEN
                    RAISE EXCEPTION 'USERNAME_EXISTS';
                END IF;
                INSERT INTO users (username, email, password_hash)
                VALUES ('{Esc(req.Username)}', '{Esc(req.Email)}', '{Esc(hash)}');
            END $$;";

		var (success, message) = await RunSqlAsync(sql);

		if (!success)
		{
			if (message.Contains("EMAIL_EXISTS")) return (false, "Email already registered.");
			if (message.Contains("USERNAME_EXISTS")) return (false, "Username already taken.");
			return (false, "Registration failed.");
		}

		return (true, "Registered successfully.");
	}

	public async Task<(bool ok, string token, string username, string message)> LoginAsync(LoginRequest req)
	{
		if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
			return (false, "", "", "Email and password are required.");

		string pwHash = HashPassword(req.Password);
		string token = GenerateToken();
		string exp = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss");

		string sql = $@"
            DO $$
            DECLARE v_id INT;
            BEGIN
                SELECT id INTO v_id
                FROM users
                WHERE email = '{Esc(req.Email)}' AND password_hash = '{Esc(pwHash)}';
 
                IF v_id IS NULL THEN
                    RAISE EXCEPTION 'INVALID_CREDENTIALS';
                END IF;
 
                DELETE FROM sessions WHERE expires_at < NOW();
 
                INSERT INTO sessions (user_id, token, expires_at)
                VALUES (v_id, '{Esc(token)}', '{exp}'::TIMESTAMPTZ);
            END $$;";

		var (success, message) = await RunSqlAsync(sql);

		if (!success)
		{
			if (message.Contains("INVALID_CREDENTIALS")) return (false, "", "", "Invalid email or password.");
			return (false, "", "", "Login failed.");
		}

		string fetchSql = $@"
            DO $$
            DECLARE v_name TEXT;
            BEGIN
                SELECT u.username INTO v_name
                FROM users u
                JOIN sessions s ON s.user_id = u.id
                WHERE s.token = '{Esc(token)}' AND s.expires_at > NOW()
                LIMIT 1;
                RAISE EXCEPTION 'USERNAME:%', COALESCE(v_name, '');
            END $$;";

		var (_, fetchMsg) = await RunSqlAsync(fetchSql);
		string username = ExtractValue(fetchMsg, "USERNAME:");

		return (true, token, username, "Login successful.");
	}

	public async Task<(bool valid, string username)> ValidateSessionAsync(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return (false, "");

		string sql = $@"
            DO $$
            DECLARE v_name TEXT;
            BEGIN
                SELECT u.username INTO v_name
                FROM sessions s
                JOIN users u ON u.id = s.user_id
                WHERE s.token = '{Esc(token)}' AND s.expires_at > NOW()
                LIMIT 1;
 
                IF v_name IS NULL THEN
                    RAISE EXCEPTION 'SESSION_INVALID';
                END IF;
 
                RAISE EXCEPTION 'USERNAME:%', v_name;
            END $$;";

		var (_, msg) = await RunSqlAsync(sql);

		if (msg.Contains("USERNAME:"))
			return (true, ExtractValue(msg, "USERNAME:"));

		return (false, "");
	}

	public async Task LogoutAsync(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return;
		await RunSqlAsync($"DELETE FROM sessions WHERE token = '{Esc(token)}';");
	}

	private static string HashPassword(string password)
	{
		using var sha = SHA256.Create();
		byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password + "auth_salt_2026"));
		return Convert.ToHexString(bytes).ToLower();
	}

	private static string GenerateToken()
	{
		var bytes = new byte[32];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
	}

	private static string Esc(string s) => s.Replace("'", "''");

	private static string ExtractValue(string msg, string prefix)
	{
		int idx = msg.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
		if (idx < 0) return "";
		return msg.Substring(idx + prefix.Length).Trim().Trim('"').Split('"')[0].Split('\n')[0].Trim();
	}

	private async Task<(bool success, string message)> RunSqlAsync(string sql)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("ProxyClient");
			string hash = AppState.DbHash;

			var payload = JsonSerializer.Serialize(new { Hash = hash, Script = sql });
			var content = new StringContent(payload, Encoding.UTF8, "application/json");
			var response = await client.PostAsync("/Home/ExecuteScript", content);
			var body = await response.Content.ReadAsStringAsync();

			using var doc = JsonDocument.Parse(body);
			bool ok = doc.RootElement.GetProperty("success").GetBoolean();
			string msg = doc.RootElement.GetProperty("message").GetString() ?? "";
			return (ok, msg);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "RunSqlAsync failed");
			return (false, ex.Message);
		}
	}
}