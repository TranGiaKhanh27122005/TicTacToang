using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Services;
using TicTacToang.Infrastructure.Persistence;
using TicTacToang.Infrastructure.Security;
using TicTacToang.Web.Components;
using TicTacToang.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = BuildConnectionString(builder.Configuration, builder.Environment.ContentRootPath, databaseProvider);
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});
builder.Services.AddSingleton<IApplicationStore, SqliteApplicationStore>();
builder.Services.AddSingleton<IPasswordService, Pbkdf2PasswordService>();
builder.Services.AddSingleton<PlayerService>();
builder.Services.AddSingleton<GomokuAi>();
builder.Services.AddSingleton<MatchService>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<AdminDashboardService>();
builder.Services.AddScoped<UserSession>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string BuildConnectionString(IConfiguration configuration, string contentRootPath, string provider)
{
    var configuredConnectionString = configuration.GetConnectionString("DefaultConnection")
        ?? configuration["DATABASE_URL"]
        ?? "Data Source=Data/tictactoang.db";

    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        return NormalizePostgresConnectionString(configuredConnectionString);
    }

    var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
    if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
    {
        connectionStringBuilder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", connectionStringBuilder.DataSource));
    }
    Directory.CreateDirectory(Path.GetDirectoryName(connectionStringBuilder.DataSource)!);
    return connectionStringBuilder.ToString();
}

static string NormalizePostgresConnectionString(string connectionString)
{
    if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return connectionString;
    }

    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        SslMode = SslMode.Require
    };
    return builder.ToString();
}
