namespace AccessIt.Api.Configuration;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "AccessIt.Api";
    public string Audience { get; set; } = "AccessIt.Web";
    public string Key { get; set; } = string.Empty;
    public int ExpiresHours { get; set; } = 12;
}

public sealed class DingTalkOptions
{
    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public long? AgentId { get; set; }
    public List<string> BootstrapAdminNames { get; set; } = [];
    public int DirectorySyncHours { get; set; } = 24;
}

public sealed class HikiotOptions
{
    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://open-api.hikiot.com";
    public string RedirectUri { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
}
