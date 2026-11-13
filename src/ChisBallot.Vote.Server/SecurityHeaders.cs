namespace ChisBallot.Vote.Server;

/// <summary>
/// Headers every response carries: no referrer is ever sent (so nothing about
/// this page leaks through a link), nothing is cached, and the page cannot be
/// framed. Nothing is cached because the browser app's files must always match
/// each other: Cloudflare keeps .js files for hours and tells browsers to do the
/// same, so after a deploy a stale dotnet.js (which holds the hash of every
/// assembly) refuses the new assemblies and the page never starts.
/// </summary>
public sealed class SecurityHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";

        // Static files set their own Cache-Control when the response starts, so
        // this is applied then, after theirs.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
