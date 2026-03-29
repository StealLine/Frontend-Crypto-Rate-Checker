using UserAuthApp;
using UserAuthApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<AuthService>();
  
builder.Services.AddHttpClient("ProxyClient", client =>
{
	string baseUrl = builder.Configuration["ProxyApi:BaseUrl"] ?? "http://localhost:5000";
	client.BaseAddress = new Uri(baseUrl);
	client.Timeout = TimeSpan.FromSeconds(30);
});

// ��� �����: ������� �� ����� ����� (���� ���� DB_HASH � env),
// ���� CREATE TABLE IF NOT EXISTS
builder.Services.AddHostedService<InitDbHostedService>();

var app = builder.Build();

app.MapControllers();

app.Run();
