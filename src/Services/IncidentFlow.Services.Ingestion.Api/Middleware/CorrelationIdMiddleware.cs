namespace IncidentFlow.Services.Ingestion.Api.Middleware;

/// <summary>
/// Reads the caller's x-correlation-id (or makes one), keeps it for the request, and echoes it on the response,
/// the same way the gateway does, so a caller that reaches Ingestion directly can still trace its request.
/// </summary>
public static class CorrelationIdMiddleware
{
    public const string Header = "x-correlation-id";

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
        ctx.Items[Header] = id;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });
        await next();
    });
}
