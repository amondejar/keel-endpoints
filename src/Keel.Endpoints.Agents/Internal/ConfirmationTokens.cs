using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>
/// The MRTR request state of a pending confirmation: signed and encrypted with Data Protection, time-limited, and bound
/// to the tool, a hash of its arguments and the user. A client cannot forge one, reuse it for other arguments or another
/// user, or keep it past its lifetime.
/// </summary>
internal sealed class ConfirmationTokens(IDataProtectionProvider provider, IOptions<KeelAgentsOptions> options, TimeProvider time)
{
    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("Keel.Endpoints.Agents.Confirmation.v1").ToTimeLimitedDataProtector();

    public string Create(string tool, string argumentsHash, string? user) =>
        _protector.Protect(Payload(tool, argumentsHash, user), time.GetUtcNow() + options.Value.ConfirmationLifetime);

    public bool IsValid(string token, string tool, string argumentsHash, string? user)
    {
        try
        {
            var payload = _protector.Unprotect(token, out var expiration);
            return expiration > time.GetUtcNow() && payload == Payload(tool, argumentsHash, user);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string Payload(string tool, string argumentsHash, string? user) => $"{tool}\n{argumentsHash}\n{user}";

    /// <summary>SHA-256 of the arguments, keys sorted at every level, so the same call hashes the same.</summary>
    public static string Hash(IDictionary<string, JsonElement>? arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in (arguments ?? new Dictionary<string, JsonElement>()).OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                Canonical(writer, value);
            }

            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Canonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Canonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
