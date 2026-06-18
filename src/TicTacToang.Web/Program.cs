using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=Data/tictactoang.db";
var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
{
    connectionStringBuilder.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", connectionStringBuilder.DataSource));
}
Directory.CreateDirectory(Path.GetDirectoryName(connectionStringBuilder.DataSource)!);
var connectionString = connectionStringBuilder.ToString();

builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(connectionString));
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
