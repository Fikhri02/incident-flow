using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentFlow.Core;

const string CorrelationIdHeader = "x-correlation-id";
const string ApiKeyHeader = "x-api-key";
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};

var engine = new IncidentFlowEngine();
var listener = new HttpListener();
var prefix = Environment.GetEnvironmentVariable("INCIDENTFLOW_URL") ?? "http://localhost:5089/";
listener.Prefixes.Add(prefix);
listener.Start();

Console.WriteLine($"IncidentFlow API listening on {prefix}");
Console.WriteLine("Press Ctrl+C to stop.");

var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
    listener.Stop();
};

while (!stopping.IsCancellationRequested)
{
    try
    {
        var context = await listener.GetContextAsync();
        _ = Task.Run(() => Handle(context, engine), stopping.Token);
    }
    catch (HttpListenerException) when (stopping.IsCancellationRequested)
    {
        break;
    }
    catch (ObjectDisposedException) when (stopping.IsCancellationRequested)
    {
        break;
    }
}

async Task Handle(HttpListenerContext context, IncidentFlowEngine engine)
{
    var request = context.Request;
    var response = context.Response;
    var correlationId = request.Headers[CorrelationIdHeader] ?? Guid.NewGuid().ToString("N");
    response.Headers[CorrelationIdHeader] = correlationId;

    try
    {
        var path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
        if (path.Length == 0)
        {
            path = "/";
        }

        if (!IsAuthorized(request, path))
        {
            await WriteJson(response, 401, new { error = "API key is required." });
            return;
        }

        var result = await Route(request, path, correlationId, engine);
        await WriteJson(response, result.StatusCode, result.Body);
    }
    catch (ArgumentException ex)
    {
        await WriteJson(response, 400, new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        var statusCode = ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ? 429 : 409;
        await WriteJson(response, statusCode, new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        await WriteJson(response, 404, new { error = ex.Message });
    }
    catch (Exception ex)
    {
        await WriteJson(response, 500, new { error = ex.Message });
    }
}

async Task<RouteResult> Route(HttpListenerRequest request, string path, string correlationId, IncidentFlowEngine engine)
{
    var method = request.HttpMethod.ToUpperInvariant();
    if (method == "GET" && path == "/")
    {
        return Ok(new
        {
            service = "IncidentFlow",
            status = "running",
            workflow = "signals -> detection -> correlation -> incident -> notification -> timeline -> recommendation -> resolution -> postmortem -> reporting"
        });
    }

    if (method == "GET" && (path == "/health/live" || path == "/health/ready"))
    {
        return Ok(new
        {
            status = path.EndsWith("live", StringComparison.OrdinalIgnoreCase) ? "live" : "ready",
            readiness = engine.GetRuntimeReadiness()
        });
    }

    if (method == "POST" && path == "/api/v1/signals/errors")
    {
        return Accepted(engine.IngestError(await ReadJson<ErrorSignalRequest>(request), correlationId));
    }

    if (method == "POST" && path == "/api/v1/signals/logs")
    {
        return Accepted(engine.IngestLog(await ReadJson<LogSignalRequest>(request), correlationId));
    }

    if (method == "POST" && path == "/api/v1/signals/metrics")
    {
        return Accepted(engine.IngestMetric(await ReadJson<MetricSignalRequest>(request), correlationId));
    }

    if (method == "POST" && path == "/api/v1/signals/traces")
    {
        return Accepted(engine.IngestTrace(await ReadJson<TraceSignalRequest>(request), correlationId));
    }

    if (method == "POST" && path == "/api/v1/signals/health-checks")
    {
        return Accepted(engine.IngestHealthCheck(await ReadJson<HealthCheckSignalRequest>(request), correlationId));
    }

    if (method == "POST" && path == "/api/v1/signals/deployments")
    {
        return Accepted(engine.IngestDeployment(await ReadJson<DeploymentSignalRequest>(request), correlationId));
    }

    if (method == "POST" && path == "/api/v1/signals/user-reports")
    {
        return Accepted(engine.IngestUserReport(await ReadJson<UserReportSignalRequest>(request), correlationId));
    }

    if (method == "GET" && path == "/api/v1/incidents")
    {
        return Ok(engine.GetIncidents());
    }

    if (method == "GET" && path == "/api/v1/incidents/open")
    {
        return Ok(engine.GetOpenIncidents());
    }

    if (method == "GET" && path == "/api/v1/events")
    {
        return Ok(engine.GetEvents());
    }

    if (method == "GET" && path == "/api/v1/events/outbox")
    {
        return Ok(engine.GetOutboxMessages());
    }

    if (method == "GET" && path == "/api/v1/events/inbox")
    {
        return Ok(engine.GetInboxMessages());
    }

    if (method == "GET" && path == "/api/v1/signals")
    {
        return Ok(engine.GetSignals());
    }

    if (method == "GET" && path == "/api/v1/runtime/readiness")
    {
        return Ok(engine.GetRuntimeReadiness());
    }

    if (method == "GET" && path == "/api/v1/reports/summary")
    {
        return Ok(engine.GetReport(ReportFilterFrom(request)));
    }

    if (method == "POST" && path == "/api/v1/reports/rebuild")
    {
        return Ok(engine.RebuildReport(ReportFilterFrom(request), correlationId));
    }

    if (method == "GET" && path == "/api/v1/reports/service-reliability")
    {
        return Ok(engine.GetReport(ReportFilterFrom(request)).TopFailingServices);
    }

    if (method == "GET" && path == "/api/v1/reports/mtta")
    {
        return Ok(new { minutes = engine.GetReport(ReportFilterFrom(request)).MeanTimeToAcknowledgeMinutes });
    }

    if (method == "GET" && path == "/api/v1/reports/mttr")
    {
        return Ok(new { minutes = engine.GetReport(ReportFilterFrom(request)).MeanTimeToResolveMinutes });
    }

    if (method == "GET" && path == "/api/v1/reports/severity-breakdown")
    {
        return Ok(engine.GetReport(ReportFilterFrom(request)).SeverityBreakdown);
    }

    if (method == "GET" && path == "/api/v1/reports/top-failing-services")
    {
        return Ok(engine.GetReport(ReportFilterFrom(request)).TopFailingServices);
    }

    if (method == "GET" && path == "/api/v1/reports/deployment-regressions")
    {
        return Ok(engine.GetReport(ReportFilterFrom(request)).DeploymentRegressionStats);
    }

    if (method == "POST" && path == "/api/v1/incidents/escalation-sweep")
    {
        return Ok(engine.RunEscalationSweep(await ReadJsonOrDefault(request, new EscalationSweepRequest()), correlationId));
    }

    if (method == "POST" && path == "/api/v1/notifications/retry-failed")
    {
        return Ok(engine.RetryFailedNotifications(correlationId));
    }

    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length >= 4 && segments[0] == "api" && segments[1] == "v1" && segments[2] == "incidents" && Guid.TryParse(segments[3], out var incidentId))
    {
        if (segments.Length == 4 && method == "GET")
        {
            var incident = engine.GetIncident(incidentId);
            return incident is null ? NotFound() : Ok(incident);
        }

        if (segments.Length == 5 && method == "GET" && segments[4] == "timeline")
        {
            return Ok(engine.GetTimeline(incidentId));
        }

        if (segments.Length == 5 && method == "GET" && segments[4] == "notifications")
        {
            return Ok(engine.GetNotifications(incidentId));
        }

        if (segments.Length == 5 && method == "GET" && segments[4] == "recommendations")
        {
            return Ok(engine.GetRecommendations(incidentId));
        }

        if (segments.Length == 7 && method == "POST" && segments[4] == "recommendations" && Guid.TryParse(segments[5], out var recommendationId) && segments[6] == "status")
        {
            return Ok(engine.UpdateRecommendationStatus(incidentId, recommendationId, await ReadJson<UpdateRecommendationStatusRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "acknowledge")
        {
            return Ok(engine.AcknowledgeIncident(incidentId, await ReadJson<AcknowledgeIncidentRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "assign")
        {
            return Ok(engine.AssignIncident(incidentId, await ReadJson<AssignIncidentRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "escalate")
        {
            return Ok(engine.EscalateIncident(incidentId, await ReadJson<EscalateIncidentRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "resolve")
        {
            return Ok(engine.ResolveIncident(incidentId, await ReadJson<ResolveIncidentRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "close")
        {
            return Ok(engine.CloseIncident(incidentId, correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "reopen")
        {
            return Ok(engine.ReopenIncident(incidentId, await ReadJson<ReopenIncidentRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "POST" && segments[4] == "postmortem")
        {
            return Ok(engine.GeneratePostmortem(incidentId, correlationId));
        }

        if (segments.Length == 5 && method == "PUT" && segments[4] == "postmortem")
        {
            return Ok(engine.UpdatePostmortem(incidentId, await ReadJson<UpdatePostmortemRequest>(request), correlationId));
        }

        if (segments.Length == 5 && method == "GET" && segments[4] == "postmortem")
        {
            var postmortem = engine.GetPostmortemForIncident(incidentId);
            return postmortem is null ? NotFound() : Ok(postmortem);
        }
    }

    return NotFound();
}

async Task<T> ReadJson<T>(HttpListenerRequest request)
{
    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
    var body = await reader.ReadToEndAsync();
    return JsonSerializer.Deserialize<T>(body, jsonOptions)
        ?? throw new ArgumentException("Request body is required.");
}

async Task<T> ReadJsonOrDefault<T>(HttpListenerRequest request, T fallback)
{
    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return fallback;
    }

    return JsonSerializer.Deserialize<T>(body, jsonOptions)
        ?? fallback;
}

ReportFilter ReportFilterFrom(HttpListenerRequest request)
{
    return new ReportFilter(request.QueryString["serviceName"], request.QueryString["environment"]);
}

bool IsAuthorized(HttpListenerRequest request, string path)
{
    var configuredApiKey = Environment.GetEnvironmentVariable("INCIDENTFLOW_API_KEY");
    if (string.IsNullOrWhiteSpace(configuredApiKey)
        || path == "/"
        || path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return request.Headers[ApiKeyHeader] == configuredApiKey;
}

async Task WriteJson(HttpListenerResponse response, int statusCode, object body)
{
    response.StatusCode = statusCode;
    response.ContentType = "application/json";
    var json = JsonSerializer.Serialize(body, jsonOptions);
    var bytes = Encoding.UTF8.GetBytes(json);
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    response.OutputStream.Close();
}

RouteResult Ok(object body) => new(200, body);
RouteResult Accepted(object body) => new(202, body);
RouteResult NotFound() => new(404, new { error = "Not found." });

internal sealed record RouteResult(int StatusCode, object Body);
