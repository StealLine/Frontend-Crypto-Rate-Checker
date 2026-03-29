using System.Text;
using System.Text.Json;

namespace UserAuthApp.Services;

public class InitDbHostedService : IHostedService
{
	private readonly IHttpClientFactory _factory;
	private readonly IConfiguration _config;
	private readonly ILogger<InitDbHostedService> _logger;

	private static readonly string HashFilePath = "secret_data/db_hash.txt";

	public InitDbHostedService(IHttpClientFactory factory, IConfiguration config, ILogger<InitDbHostedService> logger)
	{
		_factory = factory;
		_config = config;
		_logger = logger;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("InitDbHostedService: starting...");

		string hash = await GetOrCreateDbAsync();

		if (string.IsNullOrEmpty(hash))
		{
			_logger.LogError("InitDbHostedService: failed to get DB hash. App will not work correctly.");
			return;
		}

		AppState.DbHash = hash;
		_logger.LogInformation("InitDbHostedService: using DB hash {Hash}", hash);

		bool tablesOk = await EnsureTablesAsync(hash);

		if (tablesOk)
		{
			AppState.IsReady = true;
			_logger.LogInformation("InitDbHostedService: DB ready.");
		}
		else
		{
			_logger.LogError("InitDbHostedService: failed to create tables.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	private async Task<string> GetOrCreateDbAsync()
	{
		// Крок 1: файл у корені проекту
		if (File.Exists(HashFilePath))
		{
			string saved = (await File.ReadAllTextAsync(HashFilePath)).Trim();
			if (!string.IsNullOrWhiteSpace(saved))
			{
				_logger.LogInformation("InitDbHostedService: using DB hash from file.");
				return saved;
			}
		}

		// Крок 2: файлу нема — створюємо нову БД через проксі
		_logger.LogWarning("InitDbHostedService: db_hash.txt not found, calling CreateDB on proxy...");

		try
		{
			var client = _factory.CreateClient("ProxyClient");
			var response = await client.GetAsync("/Home/CreateDB");
			var body = await response.Content.ReadAsStringAsync();

			using var doc = JsonDocument.Parse(body);
			string hash = doc.RootElement.GetProperty("hash").GetString() ?? "";
			string msg = doc.RootElement.GetProperty("message").GetString() ?? "";

			if (string.IsNullOrEmpty(hash))
			{
				_logger.LogError("InitDbHostedService: CreateDB returned empty hash. Message: {Msg}", msg);
				return string.Empty;
			}

			_logger.LogInformation("InitDbHostedService: new DB created with hash {Hash}", hash);

			await File.WriteAllTextAsync(HashFilePath, hash);
			_logger.LogInformation("InitDbHostedService: hash saved to db_hash.txt.");

			return hash;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "InitDbHostedService: CreateDB request failed.");
			return string.Empty;
		}
	}

	private async Task<bool> EnsureTablesAsync(string hhash)
	{
		const string sql = @"
            CREATE TABLE IF NOT EXISTS users (
                id            SERIAL PRIMARY KEY,
                username      VARCHAR(50)  UNIQUE NOT NULL,
                email         VARCHAR(255) UNIQUE NOT NULL,
                password_hash TEXT         NOT NULL,
                created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS sessions (
                id         SERIAL PRIMARY KEY,
                user_id    INT         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token      TEXT        UNIQUE NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_sessions_token   ON sessions(token);
            CREATE INDEX IF NOT EXISTS idx_sessions_expires ON sessions(expires_at);";

		try
		{
			var client = _factory.CreateClient("ProxyClient");
			var payload = JsonSerializer.Serialize(new { hash = hhash, script = sql });
			var content = new StringContent(payload, Encoding.UTF8, "application/json");
			var reqq = await client.GetAsync("/Home/Hello");
			Console.WriteLine(await reqq.Content.ReadAsStringAsync());
			var resp = await client.PostAsync("/Home/ExecuteScript", content);
			var body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine(resp.Content.ToString());
			using var doc = JsonDocument.Parse(body);
			bool success = doc.RootElement.GetProperty("success").GetBoolean();
			string msg = doc.RootElement.GetProperty("message").GetString() ?? "";

			if (!success)
				_logger.LogError("InitDbHostedService: EnsureTables failed: {Msg}", msg);

			return success;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "InitDbHostedService: EnsureTables request failed.");
			return false;
		}
	}
}