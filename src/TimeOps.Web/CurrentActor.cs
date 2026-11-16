using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TimeOps.Application;
namespace TimeOps.Web;

public sealed class CurrentActor(IHttpContextAccessor http, AuthenticationStateProvider authentication) : ICurrentActor
{
    public string UserId => http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? authentication.GetAuthenticationStateAsync().GetAwaiter().GetResult().User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
}
