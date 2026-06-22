using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using AccessIt.Api.Configuration;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Security;

public interface IJwtTokenService
{
    string Create(ApplicationUser user);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    public string Create(ApplicationUser user)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.Key))
            throw new InvalidOperationException("Jwt:Key is not configured.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("dingtalkUserId", user.DingTalkUserId),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            config.Issuer,
            config.Audience,
            claims,
            expires: DateTime.UtcNow.AddHours(Math.Max(1, config.ExpiresHours)),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
