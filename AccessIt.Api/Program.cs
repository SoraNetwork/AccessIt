using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Hikiot;
using AccessIt.Api.Security;
using AccessIt.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<DingTalkOptions>(builder.Configuration.GetSection("DingTalk"));
builder.Services.Configure<HikiotOptions>(builder.Configuration.GetSection("Hikiot"));
builder.Services.AddDbContext<AccessItDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("AccessIt")));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys")));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("Hikiot", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<IDingTalkGateway, DingTalkGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<ISecretProtector, SecretProtector>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IHikiotGateway, HikiotGateway>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IIssuanceJobService, IssuanceJobService>();
builder.Services.AddScoped<IStandardAuthorityIssuanceService, StandardAuthorityIssuanceService>();
builder.Services.AddScoped<IFaceStorageService, FaceStorageService>();
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IDeviceSyncService, DeviceSyncService>();
builder.Services.AddScoped<IVisitorQrService, VisitorQrService>();
builder.Services.AddScoped<IHikiotTeamPeopleService, HikiotTeamPeopleService>();
builder.Services.AddHostedService<IssuanceJobWorker>();
builder.Services.AddHostedService<HikiotIssueReconcileWorker>();
builder.Services.AddHostedService<VisitorExpiryWorker>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccessItDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");
    try
    {
        logger.LogInformation("Applying pending database migrations before accepting requests.");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations are current.");
    }
    catch (Exception exception)
    {
        logger.LogCritical(exception, "Database migration failed. The application will not start against a partial schema.");
        throw;
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
