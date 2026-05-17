using IncidentFlow.Core;

var engine = new IncidentFlowEngine();
var correlationId = Guid.NewGuid().ToString("N");
var deployment = engine.IngestDeployment(new DeploymentSignalRequest(
    "deploy-bot",
    "Payment API",
    "production",
    "deploy-1.8.2",
    "1.8.2"
), correlationId);

Assert(!deployment.Duplicate, "deployment signal should be accepted");

for (var index = 0; index < 5; index++)
{
    var accepted = engine.IngestError(new ErrorSignalRequest(
        "checkout-web",
        "Payment API",
        "production",
        "Timeout while authorizing payment",
        "payment-timeout",
        DeploymentId: "deploy-1.8.2",
        Metadata: new Dictionary<string, string>
        {
            ["orderId"] = $"SO-{index + 1:000}"
        }
    ), correlationId);

    Assert(!accepted.Duplicate, $"error signal {index + 1} should be accepted");
}

var incidents = engine.GetIncidents();
Assert(incidents.Count == 1, "error spike should create one incident");

var incident = incidents.Single();
Assert(incident.Severity == IncidentSeverity.SEV2, "incident should be SEV2");
Assert(incident.Status == IncidentStatus.Open, "incident should be open");

var notifications = engine.GetNotifications(incident.IncidentId);
Assert(notifications.Count >= 2, "incident should create simulated notifications");

var recommendations = engine.GetRecommendations(incident.IncidentId);
Assert(recommendations.Any(r => r.Category == "Rollback"), "deployment-linked incident should recommend deployment inspection");
Assert(recommendations.Any(r => r.Category == "Downstream Dependency"), "timeout evidence should recommend downstream dependency inspection");

var updatedRecommendation = engine.UpdateRecommendationStatus(
    incident.IncidentId,
    recommendations.First().RecommendationId,
    new UpdateRecommendationStatusRequest("Accepted"),
    correlationId);
Assert(updatedRecommendation.Status == "Accepted", "recommendation status should update");

var timeline = engine.GetTimeline(incident.IncidentId);
Assert(timeline.Any(t => t.EntryType == "IncidentCreated"), "timeline should include incident creation");
Assert(timeline.Any(t => t.EntryType == "NotificationSent"), "timeline should include notification");
Assert(timeline.Any(t => t.EntryType == "RecommendationGenerated"), "timeline should include recommendation");

var acknowledged = engine.AcknowledgeIncident(incident.IncidentId, new AcknowledgeIncidentRequest("irfan"), correlationId);
Assert(acknowledged.Status == IncidentStatus.Acknowledged, "incident should be acknowledged");

var resolved = engine.ResolveIncident(incident.IncidentId, new ResolveIncidentRequest(
    "Rolled back Payment API to version 1.8.1 and error rate normalized.",
    "Payment API deployment 1.8.2 introduced a timeout regression."
), correlationId);
Assert(resolved.Status == IncidentStatus.Resolved, "incident should be resolved");

var postmortem = engine.GetPostmortemForIncident(incident.IncidentId);
Assert(postmortem is not null, "resolved incident should generate postmortem draft");
Assert(postmortem!.ActionItems.Count > 0, "postmortem should include action items");

var editedPostmortem = engine.UpdatePostmortem(
    incident.IncidentId,
    new UpdatePostmortemRequest(Status: "Reviewed", LessonsLearned: "Rollback readiness should be verified after payment deployments."),
    correlationId);
Assert(editedPostmortem.Status == "Reviewed", "postmortem should support review edits");

var duplicate = engine.IngestError(new ErrorSignalRequest(
    "checkout-web",
    "Payment API",
    "production",
    "Timeout while authorizing payment",
    "payment-timeout",
    DeploymentId: "deploy-1.8.2",
    IdempotencyKey: "payment-timeout-duplicate"
), correlationId);
var duplicateAgain = engine.IngestError(new ErrorSignalRequest(
    "checkout-web",
    "Payment API",
    "production",
    "Timeout while authorizing payment",
    "payment-timeout",
    DeploymentId: "deploy-1.8.2",
    IdempotencyKey: "payment-timeout-duplicate"
), correlationId);
Assert(!duplicate.Duplicate, "first idempotency-keyed signal should be accepted");
Assert(duplicateAgain.Duplicate, "second idempotency-keyed signal should be suppressed");

for (var index = 0; index < 3; index++)
{
    engine.IngestLog(new LogSignalRequest(
        "catalog-worker",
        "Catalog API",
        "production",
        "error",
        "Database timeout while loading product catalog",
        Fingerprint: "catalog-db-timeout",
        IdempotencyKey: $"catalog-log-{index}"
    ), correlationId);
}
Assert(engine.GetIncidents().Any(i => i.AffectedService == "Catalog API"), "error log spike should create catalog incident");

var trace = engine.IngestTrace(new TraceSignalRequest(
    "checkout-web",
    "Checkout API",
    "production",
    "trace-checkout-500",
    "POST /checkout",
    3420,
    500,
    DeploymentId: "checkout-2.4.0"
), correlationId);
Assert(!trace.Duplicate, "trace signal should be accepted");
Assert(engine.GetIncidents().Any(i => i.AffectedService == "Checkout API"), "server-error trace should create checkout incident");

for (var index = 0; index < 3; index++)
{
    engine.IngestHealthCheck(new HealthCheckSignalRequest(
        "health-agent",
        "Auth API",
        "production",
        false,
        "Token issuer unavailable",
        IdempotencyKey: $"auth-health-{index}"
    ), correlationId);
}

var authIncident = engine.GetIncidents().Single(i => i.AffectedService == "Auth API");
Assert(authIncident.Severity == IncidentSeverity.SEV1, "repeated health failure should create SEV1");
var escalated = engine.RunEscalationSweep(new EscalationSweepRequest(0), correlationId);
Assert(escalated.Any(i => i.IncidentId == authIncident.IncidentId), "unacknowledged SEV1 should escalate on sweep");

var report = engine.GetReport();
Assert(report.TotalIncidents >= 4, "report should include incident count");
Assert(report.ResolvedIncidents == 1, "report should include resolved incident count");
Assert(report.TopFailingServices.ContainsKey("Payment API"), "report should include top failing service");

var paymentReport = engine.GetReport(new ReportFilter("Payment API", "production"));
Assert(paymentReport.TotalIncidents >= 1, "filtered report should include payment incident");
Assert(engine.RebuildReport(new ReportFilter("Payment API", "production"), correlationId).TotalIncidents >= 1, "report rebuild should support filters");
Assert(engine.GetOutboxMessages().Count >= engine.GetEvents().Count, "outbox should track published events");
Assert(engine.GetInboxMessages().Count > 0, "inbox should track processed consumers");
Assert(engine.GetRuntimeReadiness().ExternalAdapterBacklog.Count > 0, "runtime readiness should expose adapter backlog");

Console.WriteLine("IncidentFlow smoke scenario passed.");
Console.WriteLine($"Incident: {incident.IncidentId}");
Console.WriteLine($"Notifications: {notifications.Count}");
Console.WriteLine($"Recommendations: {recommendations.Count}");
Console.WriteLine($"Postmortem: {postmortem.PostmortemId}");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
