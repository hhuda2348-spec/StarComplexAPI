using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ✅ Railway PORT env variable — محلياً يستخدم 5126
var port = Environment.GetEnvironmentVariable("PORT") ?? "5126";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── قاعدة البيانات ─────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                      ?? "Server=127.0.0.1;Database=star_complex;User=root;Password=12345;";

builder.Services.AddDbContext<StarComplexContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
builder.Services.AddMemoryCache();
builder.Services.AddControllers();

// ── OpenAPI / Scalar ───────────────────────────────────────────
builder.Services.AddOpenApi();

// ── CORS ───────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin()
         .AllowAnyMethod()
         .AllowAnyHeader());
});

var app = builder.Build();

// ── Scalar ────────────────────────────────────────────────────
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("StarComplex API v10")
           .WithTheme(ScalarTheme.DeepSpace)
           .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// ── Middleware Pipeline ────────────────────────────────────────
app.UseStaticFiles();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();