namespace ChisBallot.Vote.Server;

/// <summary>
/// Headers every response carries: no referrer is ever sent (so nothing about
/// this page leaks through a link), API answers are never cached, and the page
/// cannot be framed.
/// </summary>
public sealed class SecurityHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
