using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace RoomCodeGrabber;

public sealed class ProtectedCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RoomCodeGrabber.monitor.v1");
    private readonly AppConfig _config;

    public ProtectedCredentialStore(AppConfig config) => _config = config;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrEmpty(_config.ProtectedMonitorCredential);

    public void Set(string? credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            Clear();
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(credential.Trim());
        try
        {
            _config.ProtectedMonitorCredential = Convert.ToBase64String(
                ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? Get()
    {
        if (!IsConfigured) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(_config.ProtectedMonitorCredential!),
                Entropy,
                DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // A credential protected by another Windows user cannot be reused.
            return null;
        }
    }

    public void Clear() => _config.ProtectedMonitorCredential = null;

    public object ToStatus() => new { credentialConfigured = IsConfigured };
}
