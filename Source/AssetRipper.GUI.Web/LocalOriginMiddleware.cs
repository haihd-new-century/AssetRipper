using AssetRipper.Import.Logging;
using Microsoft.AspNetCore.Http;

namespace AssetRipper.GUI.Web;

/// <summary>
/// Protects the local web server from cross-site requests.
/// </summary>
/// <remarks>
/// The server only listens on 127.0.0.1, but a browser can still be tricked into sending requests to it:
/// <list type="bullet">
/// <item><b>DNS rebinding:</b> a malicious domain resolves to 127.0.0.1. Rejected because the Host header is not a loopback name.</item>
/// <item><b>CSRF:</b> a malicious page submits a form (no CORS preflight) to <c>/Export/...</c> or <c>/LoadFile</c>.
/// Rejected because state-changing requests must come from the same origin
/// (<c>Origin</c>/<c>Referer</c> are loopback, or <c>Sec-Fetch-Site</c> is <c>same-origin</c>/<c>none</c>).</item>
/// </list>
/// Requests without any browser headers (curl, scripts, the CLI tests) are allowed, since they cannot be forged by a web page.
/// </remarks>
internal sealed class LocalOriginMiddleware : IMiddleware
{
	public Task InvokeAsync(HttpContext context, RequestDelegate next)
	{
		HttpRequest request = context.Request;
		if (!IsLoopbackHost(request.Host.Host))
		{
			return Reject(context, $"host '{request.Host.Host}'");
		}
		if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
			&& !IsSameOriginRequest(request, out string? reason))
		{
			return Reject(context, reason);
		}
		return next(context);
	}

	internal static bool IsLoopbackHost(string host) => host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

	internal static bool IsSameOriginRequest(HttpRequest request, [NotNullWhen(false)] out string? reason)
	{
		string? fetchSite = request.Headers["Sec-Fetch-Site"];
		if (fetchSite is "cross-site" or "same-site")
		{
			reason = $"Sec-Fetch-Site '{fetchSite}'";
			return false;
		}

		string? origin = request.Headers.Origin;
		if (!string.IsNullOrEmpty(origin))
		{
			// "null" is sent by sandboxed iframes and some redirects; treat it as foreign.
			if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri) || !IsLoopbackHost(originUri.Host))
			{
				reason = $"origin '{origin}'";
				return false;
			}
		}
		else
		{
			string? referer = request.Headers.Referer;
			if (!string.IsNullOrEmpty(referer) && (!Uri.TryCreate(referer, UriKind.Absolute, out Uri? refererUri) || !IsLoopbackHost(refererUri.Host)))
			{
				reason = $"referer '{referer}'";
				return false;
			}
		}

		reason = null;
		return true;
	}

	private static Task Reject(HttpContext context, string what)
	{
		Logger.Warning(LogCategory.General, $"Blocked {context.Request.Method} {context.Request.Path} from {what}: cross-site requests are not allowed.");
		context.Response.StatusCode = StatusCodes.Status403Forbidden;
		return context.Response.WriteAsync("Forbidden: cross-site request");
	}
}
