using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TransitGames.Server.Lobbies;

namespace TransitGames.Server.Auth;

/// <summary>
/// Authenticates SignalR connections with the device token issued when creating/joining a game. The token comes
/// as "Authorization: Bearer ..." or, for WebSockets (browsers and SignalR clients cannot set headers there),
/// as the <c>access_token</c> query parameter.
/// </summary>
public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LobbyService lobbies)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceToken";
    public const string DeviceIdClaim = "device";
    public const string SessionIdClaim = "session";
    public const string RoleIdClaim = "role";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Query["access_token"].ToString();
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var device = await lobbies.AuthenticateAsync(token);
        if (device is null)
        {
            return AuthenticateResult.Fail("Unknown device token.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(DeviceIdClaim, device.DeviceId.ToString()),
                new Claim(SessionIdClaim, device.SessionId.ToString()),
                new Claim(RoleIdClaim, device.RoleId),
                new Claim(ClaimTypes.Name, device.Name),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    public static DeviceIdentity GetDevice(ClaimsPrincipal user) => new(
        Guid.Parse(user.FindFirstValue(DeviceIdClaim)!),
        Guid.Parse(user.FindFirstValue(SessionIdClaim)!),
        user.FindFirstValue(RoleIdClaim)!,
        user.FindFirstValue(ClaimTypes.Name)!);
}
