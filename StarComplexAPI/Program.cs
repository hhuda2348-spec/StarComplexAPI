using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. جعل السيرفر يستمع لجميع الـ IPs لضمان وصول الموبايل إليه
// (هذا السطر يضمن أن السيرفر يقبل اتصالات من أي IP على الشبكة المحلية)
builder.WebHost.UseUrls("http://0.0.0.0:5126");

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

// 2. تفعيل Scalar للجميع خلال فترة التطوير والبروفة
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("StarComplex API v10")
           .WithTheme(ScalarTheme.DeepSpace)
           .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// ── Middleware Pipeline ────────────────────────────────────────
// الترتيب الصحيح للميدل وير
app.UseStaticFiles();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();