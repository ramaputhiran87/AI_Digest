using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AI.Digest.Api.Services;

public static class AiAdminAccess
{
    public static IResult? Validate(HttpContext context, string? expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken) ||
            Encoding.UTF8.GetByteCount(expectedToken) < 32)
        {
            return Results.Json(
                new { error = "AI administrator access is not configured on the backend." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!context.Request.IsHttps &&
            (context.Connection.RemoteIpAddress is not { } remoteAddress ||
             !IPAddress.IsLoopback(remoteAddress)))
        {
            return Results.Json(
                new { error = "Administrator configuration requires HTTPS." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        const string prefix = "Bearer ";
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        var suppliedToken = authorization[prefix.Length..].Trim();
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash)
            ? null
            : Unauthorized();
    }

    private static IResult Unauthorized() =>
        Results.Json(
            new { error = "Administrator authentication is required." },
            statusCode: StatusCodes.Status401Unauthorized);
}
