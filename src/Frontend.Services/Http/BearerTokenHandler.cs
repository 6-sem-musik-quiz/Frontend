using System.Net.Http.Headers;

using Frontend.Services.Auth;

namespace Frontend.Services.Http;

// Lægger brugerens JWT på alle udgående kald som "Authorization: Bearer <token>".
public class BearerTokenHandler(IAccessTokenProvider tokenProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = tokenProvider.GetAccessToken();
        if (!string.IsNullOrWhiteSpace(token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
