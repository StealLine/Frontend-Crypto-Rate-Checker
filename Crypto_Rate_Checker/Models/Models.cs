namespace UserAuthApp.Models;

public class RegisterRequest
{
	public string Username { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
	public string Email { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
}

public class User
{
	public int Id { get; set; }
	public string Username { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string PasswordHash { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
}

public class Session
{
	public int Id { get; set; }
	public int UserId { get; set; }
	public string Token { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public DateTime ExpiresAt { get; set; }
}

public class AppSettings
{
	public string ProxyApiUrl { get; set; } = string.Empty;
	public string ProxyApiHash { get; set; } = string.Empty;
}
