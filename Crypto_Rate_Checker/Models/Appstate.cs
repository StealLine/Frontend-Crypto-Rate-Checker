namespace UserAuthApp;

/// <summary>
/// Зберігає хеш бази даних отриманий від проксі при старті.
/// </summary>
public static class AppState
{
	public static string DbHash { get; set; } = string.Empty;
	public static bool IsReady { get; set; } = false;
}
