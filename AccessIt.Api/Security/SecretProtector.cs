using Microsoft.AspNetCore.DataProtection;

namespace AccessIt.Api.Security;

public interface ISecretProtector
{
    string Protect(string value);
    string? Unprotect(string? protectedValue);
}

public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("AccessIt.HikiotSecrets.v1");

    public string Protect(string value) => _protector.Protect(value);

    public string? Unprotect(string? protectedValue) => string.IsNullOrWhiteSpace(protectedValue) ? null : _protector.Unprotect(protectedValue);
}
