using Microsoft.AspNetCore.Mvc;
using UserAuthApp.Models;
using UserAuthApp.Services;

namespace UserAuthApp.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class AuthController : ControllerBase
{
	private readonly AuthService _auth;

	public AuthController(AuthService auth) => _auth = auth;

	[HttpPost]
	public async Task<IActionResult> Register([FromBody] RegisterRequest req)
	{
		var (ok, message) = await _auth.RegisterAsync(req);
		return Ok(new { success = ok, message });
	}

	[HttpPost]
	public async Task<IActionResult> Login([FromBody] LoginRequest req)
	{
		var (ok, token, username, message) = await _auth.LoginAsync(req);
		if (!ok) return Ok(new { success = false, message });

		Response.Cookies.Append("session_token", token, new CookieOptions
		{
			HttpOnly = true,
			Secure = false, // set true in production with HTTPS
			SameSite = SameSiteMode.Strict,
			Expires = DateTimeOffset.UtcNow.AddHours(24)
		});

		return Ok(new { success = true, message, username });
	}

	[HttpPost]
	public async Task<IActionResult> Logout()
	{
		string? token = Request.Cookies["session_token"];
		if (!string.IsNullOrEmpty(token))
			await _auth.LogoutAsync(token);

		Response.Cookies.Delete("session_token");
		return Ok(new { success = true, message = "Logged out." });
	}

	[HttpGet]
	public async Task<IActionResult> Me()
	{
		string? token = Request.Cookies["session_token"];
		if (string.IsNullOrEmpty(token))
			return Ok(new { authenticated = false });

		var (valid, username) = await _auth.ValidateSessionAsync(token);
		return Ok(new { authenticated = valid, username });
	}
}