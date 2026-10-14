using Microsoft.AspNetCore.Http;

namespace AssetRipper.GUI.Web.Tests;

public class LocalOriginMiddlewareTests
{
	private static async Task<int> SendAsync(string method, string host, params (string Name, string Value)[] headers)
	{
		DefaultHttpContext context = new();
		context.Request.Method = method;
		context.Request.Host = new HostString(host);
		context.Request.Path = "/Export/UnityProject";
		foreach ((string name, string value) in headers)
		{
			context.Request.Headers[name] = value;
		}
		context.Response.Body = new MemoryStream();
		bool reached = false;
		await new LocalOriginMiddleware().InvokeAsync(context, _ => { reached = true; return Task.CompletedTask; });
		return reached ? 200 : context.Response.StatusCode;
	}

	[TestCase("GET", "127.0.0.1:8080")]
	[TestCase("GET", "localhost:8080")]
	[TestCase("POST", "127.0.0.1:8080")]
	public async Task LocalRequestsWithoutBrowserHeadersAreAllowed(string method, string host)
	{
		Assert.That(await SendAsync(method, host), Is.EqualTo(200));
	}

	[Test]
	public async Task SameOriginFormPostIsAllowed()
	{
		int status = await SendAsync("POST", "127.0.0.1:8080", ("Origin", "http://127.0.0.1:8080"), ("Sec-Fetch-Site", "same-origin"));
		Assert.That(status, Is.EqualTo(200));
	}

	[TestCase("evil.example:8080")]
	[TestCase("127.0.0.1.nip.io:8080")]
	public async Task NonLoopbackHostIsRejected(string host)
	{
		Assert.That(await SendAsync("GET", host), Is.EqualTo(403));
	}

	[TestCase("Origin", "https://evil.example")]
	[TestCase("Origin", "null")]
	[TestCase("Referer", "https://evil.example/page")]
	[TestCase("Sec-Fetch-Site", "cross-site")]
	[TestCase("Sec-Fetch-Site", "same-site")]
	public async Task CrossSitePostIsRejected(string header, string value)
	{
		Assert.That(await SendAsync("POST", "127.0.0.1:8080", (header, value)), Is.EqualTo(403));
	}

	[Test]
	public async Task CrossSiteGetIsAllowedBecauseItCannotChangeState()
	{
		Assert.That(await SendAsync("GET", "127.0.0.1:8080", ("Sec-Fetch-Site", "cross-site")), Is.EqualTo(200));
	}
}
