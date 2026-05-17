using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentFlow.Core;

public sealed class IncidentFlowEngine
{
    private const int ErrorSpikeThreshold = 5;
    private const int LogErrorSpikeThreshold = 3;
    private const int SourceRateLimitPerMinute = 1000;
    private static readonly TimeSpan ErrorSpikeWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LogSpikeWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DedupeRetention = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions EventJsonOptions = CreateEventJsonOptions();

    private readonly object _gate = new();
    private readonly List<EventEnvelope> _events = new();
    private readonly List<OutboxMessage> _outbox = new();
    private readonly List<InboxMessage> _inbox = new();
    private readonly List<SignalRecord> _signals = new();
    private readonly List<AnomalyRecord> _anomalies = new();
    private readonly List<IncidentRecord> _incidents = new();
    private readonly List<TimelineEntry> _timeline = new();
    private readonly List<NotificationRecord> _notifications = new();
    private readonly List<RecommendationRecord> _recommendations = new();
    private readonly List<PostmortemRecord> _postmortems = new();
    private readonly Dictionary<string, DateTimeOffset> _signalDedupe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _activeIncidentByProblemKey = new(StringComparer.OrdinalIgnoreCase);

    public SignalAcceptedResponse IngestError(ErrorSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        RequireText(request.Message, "Error message is required.");
        RequireText(request.Fingerprint, "Error fingerprint is required.");

        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var dedupeKey = BuildDedupeKey(
                "error",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                $"{request.Fingerprint}:{request.TraceId}:{request.DeploymentId}",
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate signal suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "error",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                request.Fingerprint,
                request.TraceId,
                request.DeploymentId,
                occurredAt,
                request.Message,
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("ErrorSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "SignalReceived", $"Error signal received from {signal.ServiceName}: {signal.Summary}", occurredAt, correlationId);
            EvaluateErrorSpike(signal, correlationId);
            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Signal accepted.");
        }
    }

    public SignalAcceptedResponse IngestLog(LogSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        RequireText(request.Severity, "Log severity is required.");
        RequireText(request.Message, "Log message is required.");

        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var fingerprint = string.IsNullOrWhiteSpace(request.Fingerprint)
                ? StableTextKey(request.Message)
                : request.Fingerprint;
            var dedupeKey = BuildDedupeKey(
                "log",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                $"{request.Severity}:{fingerprint}:{request.TraceId}",
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate log signal suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "log",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                fingerprint,
                request.TraceId,
                request.DeploymentId,
                occurredAt,
                $"{request.Severity}: {request.Message}",
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("LogSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "SignalReceived", $"Log signal received from {signal.ServiceName}: {signal.Summary}", occurredAt, correlationId);
            EvaluateLogSpike(signal, request.Severity, correlationId);
            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Log accepted.");
        }
    }

    public SignalAcceptedResponse IngestMetric(MetricSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        RequireText(request.MetricName, "Metric name is required.");

        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var dedupeKey = BuildDedupeKey(
                "metric",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                $"{request.MetricName}:{request.Value}",
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate metric suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "metric",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                $"{request.MetricName}:{request.Value}",
                null,
                null,
                occurredAt,
                $"{request.MetricName}={request.Value}",
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("MetricSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "SignalReceived", $"Metric signal received from {signal.ServiceName}: {signal.Summary}", signal.OccurredAt, correlationId);

            if (request.MetricName.Equals("p95_latency_ms", StringComparison.OrdinalIgnoreCase) && request.Value > 2000)
            {
                CreateAnomaly(signal, IncidentSeverity.SEV2, "LatencySpikeRule", "p95 latency exceeded 2 seconds.", correlationId);
            }

            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Metric accepted.");
        }
    }

    public SignalAcceptedResponse IngestTrace(TraceSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        RequireText(request.TraceId, "Trace ID is required.");
        RequireText(request.OperationName, "Operation name is required.");

        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var fingerprint = $"{request.OperationName}:{request.StatusCode}";
            var dedupeKey = BuildDedupeKey(
                "trace",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                $"{request.TraceId}:{fingerprint}",
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate trace suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "trace",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                fingerprint,
                request.TraceId,
                request.DeploymentId,
                occurredAt,
                $"{request.OperationName} completed with {request.StatusCode} in {request.DurationMs:0} ms",
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("TraceSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "SignalReceived", $"Trace signal received from {signal.ServiceName}: {signal.Summary}", signal.OccurredAt, correlationId);

            if (request.StatusCode >= 500)
            {
                CreateAnomaly(signal, IncidentSeverity.SEV2, "TraceFailureRule", "Trace returned a server error.", correlationId);
            }
            else if (request.DurationMs > 3000)
            {
                CreateAnomaly(signal, IncidentSeverity.SEV3, "TraceLatencyRule", "Trace duration exceeded 3 seconds.", correlationId);
            }

            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Trace accepted.");
        }
    }

    public SignalAcceptedResponse IngestHealthCheck(HealthCheckSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var dedupeKey = BuildDedupeKey(
                "health-check",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                $"{request.IsHealthy}:{request.Reason}",
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate health check suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "health-check",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                request.IsHealthy ? "healthy" : "unhealthy",
                null,
                null,
                occurredAt,
                request.IsHealthy ? "Health check passed." : request.Reason ?? "Health check failed.",
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("HealthCheckSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "SignalReceived", $"Health check received from {signal.ServiceName}: {signal.Summary}", signal.OccurredAt, correlationId);

            if (!request.IsHealthy)
            {
                var recentFailures = _signals
                    .Where(s => s.SignalType == "health-check"
                        && s.ServiceName.Equals(signal.ServiceName, StringComparison.OrdinalIgnoreCase)
                        && s.Environment.Equals(signal.Environment, StringComparison.OrdinalIgnoreCase)
                        && s.Fingerprint == "unhealthy"
                        && s.OccurredAt >= signal.OccurredAt.AddMinutes(-5))
                    .ToList();

                if (recentFailures.Count >= 3)
                {
                    CreateAnomaly(signal, IncidentSeverity.SEV1, "RepeatedHealthCheckFailureRule", "Health check failed 3 times in 5 minutes.", correlationId);
                }
            }

            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Health check accepted.");
        }
    }

    public SignalAcceptedResponse IngestDeployment(DeploymentSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        RequireText(request.DeploymentId, "Deployment ID is required.");
        RequireText(request.Version, "Deployment version is required.");

        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var dedupeKey = BuildDedupeKey(
                "deployment",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                $"{request.DeploymentId}:{request.Version}",
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate deployment signal suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "deployment",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                request.DeploymentId,
                null,
                request.DeploymentId,
                occurredAt,
                $"Deployment {request.Version} completed.",
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("DeploymentSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "DeploymentSignalReceived", $"Deployment signal received for {signal.ServiceName}: {signal.Summary}", signal.OccurredAt, correlationId);
            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "Deployment signal accepted.");
        }
    }

    public SignalAcceptedResponse IngestUserReport(UserReportSignalRequest request, string correlationId)
    {
        ValidateCommon(request.SourceSystem, request.ServiceName, request.Environment);
        RequireText(request.Summary, "User report summary is required.");

        lock (_gate)
        {
            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            var metadata = request.Metadata ?? new Dictionary<string, string>();
            var dedupeKey = BuildDedupeKey(
                "user-report",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.IdempotencyKey,
                StableTextKey(request.Summary),
                occurredAt,
                metadata);

            if (SuppressDuplicate(dedupeKey, occurredAt))
            {
                return new SignalAcceptedResponse(Guid.Empty, correlationId, true, "Duplicate user report suppressed.");
            }

            EnsureUnderSourceRateLimit(request.SourceSystem, request.ServiceName, request.Environment, occurredAt);
            var signal = new SignalRecord(
                Guid.NewGuid(),
                "user-report",
                request.SourceSystem,
                request.ServiceName,
                request.Environment,
                request.Region,
                StableTextKey(request.Summary),
                null,
                null,
                occurredAt,
                request.Summary,
                metadata,
                request.IdempotencyKey
            );

            _signals.Add(signal);
            Publish("UserReportSignalReceived", correlationId, null, "IncidentFlow.Ingestion", signal);
            RecordTimeline(null, "SignalReceived", $"User report received for {signal.ServiceName}: {signal.Summary}", signal.OccurredAt, correlationId);
            return new SignalAcceptedResponse(signal.SignalId, correlationId, false, "User report accepted.");
        }
    }

    public IReadOnlyList<IncidentRecord> GetIncidents()
    {
        lock (_gate)
        {
            return _incidents.ToList();
        }
    }

    public IReadOnlyList<IncidentRecord> GetOpenIncidents()
    {
        lock (_gate)
        {
            return _incidents.Where(i => i.Status is not IncidentStatus.Resolved and not IncidentStatus.Closed).ToList();
        }
    }

    public IncidentRecord? GetIncident(Guid incidentId)
    {
        lock (_gate)
        {
            return _incidents.FirstOrDefault(i => i.IncidentId == incidentId);
        }
    }

    public IncidentRecord AcknowledgeIncident(Guid incidentId, AcknowledgeIncidentRequest request, string correlationId)
    {
        lock (_gate)
        {
            RequireText(request.EngineerId, "Engineer ID is required.");
            var incident = RequireIncident(incidentId);
            if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed)
            {
                throw new InvalidOperationException("Resolved or closed incidents cannot be acknowledged.");
            }

            var updated = incident with
            {
                Status = IncidentStatus.Acknowledged,
                AssignedEngineerId = string.IsNullOrWhiteSpace(incident.AssignedEngineerId) ? request.EngineerId : incident.AssignedEngineerId,
                AcknowledgedAt = DateTimeOffset.UtcNow
            };

            ReplaceIncident(updated);
            Publish("IncidentAcknowledged", correlationId, incidentId.ToString(), "IncidentFlow.Incident", updated);
            RecordTimeline(incidentId, "IncidentAcknowledged", $"Incident acknowledged by {request.EngineerId}.", updated.AcknowledgedAt.Value, correlationId);
            UpdateReports();
            return updated;
        }
    }

    public IncidentRecord AssignIncident(Guid incidentId, AssignIncidentRequest request, string correlationId)
    {
        lock (_gate)
        {
            RequireText(request.EngineerId, "Engineer ID is required.");
            var incident = RequireIncident(incidentId);
            var updated = incident with
            {
                AssignedEngineerId = request.EngineerId,
                OwnerTeamId = request.TeamId ?? incident.OwnerTeamId
            };
            ReplaceIncident(updated);
            Publish("IncidentAssigned", correlationId, incidentId.ToString(), "IncidentFlow.Incident", updated);
            RecordTimeline(incidentId, "IncidentAssigned", $"Incident assigned to {request.EngineerId}.", DateTimeOffset.UtcNow, correlationId);
            return updated;
        }
    }

    public IncidentRecord EscalateIncident(Guid incidentId, EscalateIncidentRequest request, string correlationId)
    {
        lock (_gate)
        {
            RequireText(request.Reason, "Escalation reason is required.");
            var incident = RequireIncident(incidentId);
            if (incident.Status == IncidentStatus.Closed)
            {
                throw new InvalidOperationException("Closed incidents cannot be escalated.");
            }

            var updated = incident with { Severity = request.Severity, Status = IncidentStatus.Mitigating };
            ReplaceIncident(updated);
            Publish("IncidentEscalated", correlationId, incidentId.ToString(), "IncidentFlow.Incident", new { Incident = updated, request.Reason });
            RecordTimeline(incidentId, "IncidentEscalated", $"Incident escalated to {request.Severity}: {request.Reason}", DateTimeOffset.UtcNow, correlationId);
            NotifyIncident(updated, "escalated", correlationId);
            UpdateReports();
            return updated;
        }
    }

    public IncidentRecord ResolveIncident(Guid incidentId, ResolveIncidentRequest request, string correlationId)
    {
        lock (_gate)
        {
            RequireText(request.ResolutionSummary, "Resolution summary is required.");
            var incident = RequireIncident(incidentId);
            var resolvedAt = DateTimeOffset.UtcNow;
            var updated = incident with
            {
                Status = IncidentStatus.Resolved,
                ResolvedAt = resolvedAt,
                ResolutionSummary = request.ResolutionSummary,
                RootCauseSummary = request.RootCauseSummary ?? incident.RootCauseSummary
            };

            ReplaceIncident(updated);
            foreach (var key in _activeIncidentByProblemKey.Where(pair => pair.Value == incidentId).Select(pair => pair.Key).ToList())
            {
                _activeIncidentByProblemKey.Remove(key);
            }

            Publish("IncidentResolved", correlationId, incidentId.ToString(), "IncidentFlow.Incident", updated);
            RecordTimeline(incidentId, "IncidentResolved", $"Incident resolved: {request.ResolutionSummary}", resolvedAt, correlationId);
            NotifyIncident(updated, "resolved", correlationId);
            UpdateReports();
            GeneratePostmortem(incidentId, correlationId);
            return updated;
        }
    }

    public IncidentRecord CloseIncident(Guid incidentId, string correlationId)
    {
        lock (_gate)
        {
            var incident = RequireIncident(incidentId);
            if (string.IsNullOrWhiteSpace(incident.ResolutionSummary))
            {
                throw new InvalidOperationException("Incident cannot be closed without a resolution summary.");
            }

            var updated = incident with { Status = IncidentStatus.Closed, ClosedAt = DateTimeOffset.UtcNow };
            ReplaceIncident(updated);
            Publish("IncidentClosed", correlationId, incidentId.ToString(), "IncidentFlow.Incident", updated);
            RecordTimeline(incidentId, "IncidentClosed", "Incident closed.", updated.ClosedAt.Value, correlationId);
            UpdateReports();
            return updated;
        }
    }

    public IncidentRecord ReopenIncident(Guid incidentId, ReopenIncidentRequest request, string correlationId)
    {
        lock (_gate)
        {
            RequireText(request.Reason, "Reopen reason is required.");
            var incident = RequireIncident(incidentId);
            var updated = incident with { Status = IncidentStatus.Reopened, ReopenReason = request.Reason, ResolvedAt = null, ClosedAt = null };
            ReplaceIncident(updated);
            Publish("IncidentReopened", correlationId, incidentId.ToString(), "IncidentFlow.Incident", new { Incident = updated, request.Reason });
            RecordTimeline(incidentId, "IncidentReopened", $"Incident reopened: {request.Reason}", DateTimeOffset.UtcNow, correlationId);
            UpdateReports();
            return updated;
        }
    }

    public IReadOnlyList<IncidentRecord> RunEscalationSweep(EscalationSweepRequest request, string correlationId)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddMinutes(-Math.Max(0, request.AcknowledgementSlaMinutes));
            var escalated = new List<IncidentRecord>();

            foreach (var incident in _incidents.Where(i =>
                         i.Severity == IncidentSeverity.SEV1
                         && i.Status is IncidentStatus.Open or IncidentStatus.Detected or IncidentStatus.Reopened
                         && !i.AcknowledgedAt.HasValue
                         && i.DetectedAt <= cutoff).ToList())
            {
                if (_timeline.Any(t => t.IncidentId == incident.IncidentId && t.EntryType == "IncidentEscalationTimerFired"))
                {
                    continue;
                }

                var updated = incident with { Status = IncidentStatus.Mitigating };
                ReplaceIncident(updated);
                Publish("IncidentEscalationTimerFired", correlationId, incident.IncidentId.ToString(), "IncidentFlow.Notification", updated);
                RecordTimeline(incident.IncidentId, "IncidentEscalationTimerFired", "Unacknowledged SEV1 incident breached acknowledgement SLA.", now, correlationId);
                NotifyIncident(updated, "escalation-timer", correlationId);
                escalated.Add(updated);
            }

            UpdateReports();
            return escalated;
        }
    }

    public IReadOnlyList<NotificationRecord> RetryFailedNotifications(string correlationId)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var retried = new List<NotificationRecord>();

            for (var index = 0; index < _notifications.Count; index++)
            {
                var notification = _notifications[index];
                if (!notification.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                    || notification.NextRetryAt.HasValue && notification.NextRetryAt.Value > now)
                {
                    continue;
                }

                var updated = notification with
                {
                    Status = "Sent",
                    SentAt = now,
                    RetryCount = notification.RetryCount + 1,
                    NextRetryAt = null
                };
                _notifications[index] = updated;
                Publish("NotificationRetried", correlationId, notification.NotificationId.ToString(), "IncidentFlow.Notification", updated);
                RecordTimeline(notification.IncidentId, "NotificationRetried", $"{updated.Channel} notification retry sent to {updated.Recipient}.", now, correlationId);
                retried.Add(updated);
            }

            return retried;
        }
    }

    public IReadOnlyList<TimelineEntry> GetTimeline(Guid incidentId)
    {
        lock (_gate)
        {
            return _timeline
                .Where(t => t.IncidentId == incidentId)
                .OrderBy(t => t.OccurredAt)
                .ToList();
        }
    }

    public IReadOnlyList<NotificationRecord> GetNotifications(Guid incidentId)
    {
        lock (_gate)
        {
            return _notifications.Where(n => n.IncidentId == incidentId).OrderBy(n => n.SentAt).ToList();
        }
    }

    public IReadOnlyList<RecommendationRecord> GetRecommendations(Guid incidentId)
    {
        lock (_gate)
        {
            return _recommendations.Where(r => r.IncidentId == incidentId).OrderByDescending(r => r.Confidence).ToList();
        }
    }

    public RecommendationRecord UpdateRecommendationStatus(Guid incidentId, Guid recommendationId, UpdateRecommendationStatusRequest request, string correlationId)
    {
        lock (_gate)
        {
            RequireIncident(incidentId);
            RequireText(request.Status, "Recommendation status is required.");
            var index = _recommendations.FindIndex(r => r.IncidentId == incidentId && r.RecommendationId == recommendationId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Recommendation {recommendationId} was not found.");
            }

            var updated = _recommendations[index] with { Status = request.Status };
            _recommendations[index] = updated;
            Publish("RecommendationStatusUpdated", correlationId, recommendationId.ToString(), "IncidentFlow.Recommendation", updated);
            RecordTimeline(incidentId, "RecommendationStatusUpdated", $"Recommendation status changed to {request.Status}: {updated.Title}", DateTimeOffset.UtcNow, correlationId);
            return updated;
        }
    }

    public PostmortemRecord GeneratePostmortem(Guid incidentId, string correlationId)
    {
        lock (_gate)
        {
            var existing = _postmortems.FirstOrDefault(p => p.IncidentId == incidentId);
            if (existing is not null)
            {
                return existing;
            }

            var incident = RequireIncident(incidentId);
            var timelineText = string.Join(Environment.NewLine, GetTimeline(incidentId).Select(t => $"- {t.OccurredAt:u} {t.Message}"));
            var recommendations = GetRecommendations(incidentId);
            var postmortem = new PostmortemRecord(
                Guid.NewGuid(),
                incidentId,
                "Draft",
                incident.Description,
                $"{incident.AffectedService} in {incident.Environment} was impacted by {incident.Title}.",
                timelineText,
                incident.RootCauseSummary ?? "Root cause pending operator review.",
                incident.ResolutionSummary ?? "Resolution pending.",
                "Detection, notification, timeline, and reporting were generated automatically.",
                "Operator review is still required before approval.",
                recommendations.Select(r => new ActionItem(r.Title, r.Detail, incident.OwnerTeamId ?? "platform-team", null, "Open")).ToList(),
                "Use deterministic signals, timeline evidence, and previous fingerprints to improve response quality.",
                DateTimeOffset.UtcNow
            );

            _postmortems.Add(postmortem);
            Publish("PostmortemCreated", correlationId, incidentId.ToString(), "IncidentFlow.Postmortem", postmortem);
            RecordTimeline(incidentId, "PostmortemCreated", "Postmortem draft generated.", postmortem.CreatedAt, correlationId);
            return postmortem;
        }
    }

    public PostmortemRecord UpdatePostmortem(Guid incidentId, UpdatePostmortemRequest request, string correlationId)
    {
        lock (_gate)
        {
            var postmortem = GetPostmortemForIncident(incidentId) ?? GeneratePostmortem(incidentId, correlationId);
            var index = _postmortems.FindIndex(p => p.PostmortemId == postmortem.PostmortemId);
            var updated = postmortem with
            {
                Status = request.Status ?? postmortem.Status,
                Summary = request.Summary ?? postmortem.Summary,
                Impact = request.Impact ?? postmortem.Impact,
                RootCause = request.RootCause ?? postmortem.RootCause,
                Resolution = request.Resolution ?? postmortem.Resolution,
                WhatWentWell = request.WhatWentWell ?? postmortem.WhatWentWell,
                WhatWentWrong = request.WhatWentWrong ?? postmortem.WhatWentWrong,
                LessonsLearned = request.LessonsLearned ?? postmortem.LessonsLearned,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _postmortems[index] = updated;
            Publish("PostmortemUpdated", correlationId, incidentId.ToString(), "IncidentFlow.Postmortem", updated);
            RecordTimeline(incidentId, "PostmortemUpdated", $"Postmortem updated: {updated.Status}.", updated.UpdatedAt.Value, correlationId);
            return updated;
        }
    }

    public PostmortemRecord? GetPostmortemForIncident(Guid incidentId)
    {
        lock (_gate)
        {
            return _postmortems.FirstOrDefault(p => p.IncidentId == incidentId);
        }
    }

    public IncidentSummaryReport GetReport(ReportFilter? filter = null)
    {
        lock (_gate)
        {
            return BuildReport(FilterIncidents(filter).ToList());
        }
    }

    public IncidentSummaryReport RebuildReport(ReportFilter? filter, string correlationId)
    {
        lock (_gate)
        {
            var report = BuildReport(FilterIncidents(filter).ToList());
            Publish("ReportingProjectionRebuilt", correlationId, null, "IncidentFlow.Reporting", report);
            return report;
        }
    }

    public IReadOnlyList<EventEnvelope> GetEvents()
    {
        lock (_gate)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<OutboxMessage> GetOutboxMessages()
    {
        lock (_gate)
        {
            return _outbox.ToList();
        }
    }

    public IReadOnlyList<InboxMessage> GetInboxMessages()
    {
        lock (_gate)
        {
            return _inbox.ToList();
        }
    }

    public IReadOnlyList<SignalRecord> GetSignals()
    {
        lock (_gate)
        {
            return _signals.ToList();
        }
    }

    public RuntimeReadinessReport GetRuntimeReadiness()
    {
        return new RuntimeReadinessReport(
            "net6.0 local profile; net8.0 upgrade is blocked until .NET 8 SDK is installed.",
            "HttpListener local gateway with stable REST routes; YARP adapter remains the production replacement.",
            "In-memory event bus plus outbox/inbox records; RabbitMQ adapter remains the production replacement.",
            "In-memory dedupe, rate limits, and windows; Redis adapter remains the production replacement.",
            "In-memory stores; PostgreSQL/EF Core adapters remain the production replacement.",
            "Correlation IDs, health endpoints, event stream, and timeline are available; OpenTelemetry adapter remains the production replacement.",
            "Dependency-free smoke and contract runners are available; xUnit/Testcontainers/Reqnroll remain the production replacement.",
            new[]
            {
                "Install .NET 8 SDK and pin with global.json.",
                "Restore external packages for Aspire, YARP, RabbitMQ, Redis, PostgreSQL, EF Core, OpenTelemetry, and test frameworks.",
                "Replace local adapters behind the existing contracts without changing endpoint behavior.",
                "Run the smoke and contract runners as regression gates during the adapter migration."
            }
        );
    }

    private void EvaluateErrorSpike(SignalRecord signal, string correlationId)
    {
        var recent = _signals
            .Where(s => s.SignalType == "error"
                && s.ServiceName.Equals(signal.ServiceName, StringComparison.OrdinalIgnoreCase)
                && s.Environment.Equals(signal.Environment, StringComparison.OrdinalIgnoreCase)
                && s.Fingerprint == signal.Fingerprint
                && s.OccurredAt >= signal.OccurredAt.Subtract(ErrorSpikeWindow))
            .ToList();

        if (recent.Count >= ErrorSpikeThreshold)
        {
            CreateAnomaly(signal, IncidentSeverity.SEV2, "ErrorSpikeRule", $"{recent.Count} matching errors detected within {ErrorSpikeWindow.TotalMinutes:0} minutes.", correlationId);
        }
    }

    private void EvaluateLogSpike(SignalRecord signal, string severity, string correlationId)
    {
        if (!severity.Equals("error", StringComparison.OrdinalIgnoreCase)
            && !severity.Equals("fatal", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var recent = _signals
            .Where(s => s.SignalType == "log"
                && s.ServiceName.Equals(signal.ServiceName, StringComparison.OrdinalIgnoreCase)
                && s.Environment.Equals(signal.Environment, StringComparison.OrdinalIgnoreCase)
                && s.Fingerprint == signal.Fingerprint
                && s.OccurredAt >= signal.OccurredAt.Subtract(LogSpikeWindow))
            .ToList();

        if (recent.Count >= LogErrorSpikeThreshold)
        {
            var incidentSeverity = severity.Equals("fatal", StringComparison.OrdinalIgnoreCase)
                ? IncidentSeverity.SEV1
                : IncidentSeverity.SEV2;
            CreateAnomaly(signal, incidentSeverity, "LogErrorSpikeRule", $"{recent.Count} matching error logs detected within {LogSpikeWindow.TotalMinutes:0} minutes.", correlationId);
        }
    }

    private void CreateAnomaly(SignalRecord signal, IncidentSeverity severity, string ruleName, string summary, string correlationId)
    {
        RecordInboxProcessed("IncidentFlow.Detection", signal.SignalId);
        var problemKey = ProblemKey(signal.ServiceName, signal.Environment, signal.Fingerprint ?? signal.SignalId.ToString());
        if (_activeIncidentByProblemKey.ContainsKey(problemKey))
        {
            return;
        }

        var matchingSignals = _signals
            .Where(s => s.ServiceName.Equals(signal.ServiceName, StringComparison.OrdinalIgnoreCase)
                && s.Environment.Equals(signal.Environment, StringComparison.OrdinalIgnoreCase)
                && s.Fingerprint == signal.Fingerprint)
            .Select(s => s.SignalId)
            .ToList();

        var anomaly = new AnomalyRecord(
            Guid.NewGuid(),
            signal.ServiceName,
            signal.Environment,
            signal.Region,
            signal.Fingerprint,
            signal.DeploymentId,
            severity,
            ruleName,
            summary,
            DateTimeOffset.UtcNow,
            matchingSignals
        );

        _anomalies.Add(anomaly);
        Publish("AnomalyDetected", correlationId, signal.SignalId.ToString(), "IncidentFlow.Detection", anomaly);
        RecordTimeline(null, "AnomalyDetected", $"{ruleName}: {summary}", anomaly.DetectedAt, correlationId);
        CorrelateAndCreateIncident(anomaly, correlationId);
    }

    private void CorrelateAndCreateIncident(AnomalyRecord anomaly, string correlationId)
    {
        RecordInboxProcessed("IncidentFlow.Correlation", anomaly.AnomalyId);
        Publish("SignalsCorrelated", correlationId, anomaly.AnomalyId.ToString(), "IncidentFlow.Correlation", new
        {
            anomaly.ServiceName,
            anomaly.Environment,
            anomaly.Fingerprint,
            Score = 100,
            Reason = "Same service, environment, fingerprint, and time window."
        });

        Publish("IncidentCandidateDetected", correlationId, anomaly.AnomalyId.ToString(), "IncidentFlow.Correlation", anomaly);
        if (!string.IsNullOrWhiteSpace(anomaly.DeploymentId))
        {
            Publish("RootCauseCandidateIdentified", correlationId, anomaly.AnomalyId.ToString(), "IncidentFlow.Correlation", new
            {
                anomaly.DeploymentId,
                Reason = "Recent deployment is attached to the failing signal."
            });
        }

        CreateIncidentFromAnomaly(anomaly, correlationId);
    }

    private void CreateIncidentFromAnomaly(AnomalyRecord anomaly, string correlationId)
    {
        RecordInboxProcessed("IncidentFlow.Incident", anomaly.AnomalyId);
        var problemKey = ProblemKey(anomaly.ServiceName, anomaly.Environment, anomaly.Fingerprint ?? anomaly.AnomalyId.ToString());
        if (_activeIncidentByProblemKey.ContainsKey(problemKey))
        {
            return;
        }

        var title = $"{anomaly.Severity} {anomaly.ServiceName} issue in {anomaly.Environment}";
        var incident = new IncidentRecord(
            Guid.NewGuid(),
            title,
            anomaly.Summary,
            anomaly.Severity,
            IncidentStatus.Open,
            anomaly.ServiceName,
            anomaly.Environment,
            anomaly.Region,
            anomaly.Severity == IncidentSeverity.SEV1 ? "platform-on-call" : "service-owner",
            null,
            anomaly.DetectedAt,
            null,
            null,
            null,
            !string.IsNullOrWhiteSpace(anomaly.DeploymentId) ? $"Deployment {anomaly.DeploymentId} is a candidate root cause." : null,
            null,
            null,
            anomaly.SignalIds
        );

        _incidents.Add(incident);
        _activeIncidentByProblemKey[problemKey] = incident.IncidentId;
        Publish("IncidentCreated", correlationId, anomaly.AnomalyId.ToString(), "IncidentFlow.Incident", incident);
        RecordTimeline(incident.IncidentId, "IncidentCreated", $"Incident opened: {incident.Title}", incident.DetectedAt, correlationId);
        NotifyIncident(incident, "created", correlationId);
        GenerateRecommendations(incident, anomaly, correlationId);
        UpdateReports();
    }

    private void NotifyIncident(IncidentRecord incident, string action, string correlationId)
    {
        RecordInboxProcessed("IncidentFlow.Notification", incident.IncidentId);
        var channels = incident.Severity == IncidentSeverity.SEV1
            ? new[] { "slack", "email", "sms", "in-app" }
            : new[] { "slack", "in-app" };

        foreach (var channel in channels)
        {
            var dedupeKey = $"{incident.IncidentId}:{channel}:{action}";
            if (_notifications.Any(n => $"{n.IncidentId}:{n.Channel}:{action}" == dedupeKey))
            {
                continue;
            }

            var notification = new NotificationRecord(
                Guid.NewGuid(),
                incident.IncidentId,
                channel,
                incident.OwnerTeamId ?? "platform-on-call",
                "Sent",
                $"Incident {incident.Title} {action}.",
                DateTimeOffset.UtcNow
            );

            _notifications.Add(notification);
            Publish("NotificationSent", correlationId, incident.IncidentId.ToString(), "IncidentFlow.Notification", notification);
            RecordTimeline(incident.IncidentId, "NotificationSent", $"{channel} notification sent to {notification.Recipient}.", notification.SentAt, correlationId);
        }
    }

    private void GenerateRecommendations(IncidentRecord incident, AnomalyRecord anomaly, string correlationId)
    {
        RecordInboxProcessed("IncidentFlow.Recommendation", incident.IncidentId);
        var recommendations = new List<RecommendationRecord>();
        var evidence = EvidenceFor(anomaly);

        if (!string.IsNullOrWhiteSpace(anomaly.DeploymentId))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Rollback", "Inspect recent deployment", $"Deployment {anomaly.DeploymentId} is linked to the anomaly. Check rollout health and prepare rollback.", 90));
        }

        if (anomaly.RuleName.Contains("Latency", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Capacity", "Inspect latency and capacity", "Latency crossed the threshold. Check saturation, thread pools, connection pools, and downstream latency.", 82));
        }

        if (anomaly.RuleName.Contains("Health", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Service Health", "Check failing health dependency", "Repeated health check failures indicate the service or one dependency is unavailable.", 85));
        }

        if (evidence.Contains("database", StringComparison.OrdinalIgnoreCase) || evidence.Contains("sql", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Database", "Inspect database dependency", "Signal evidence mentions database behavior. Check connection pool pressure, migrations, slow queries, and lock waits.", 76));
        }

        if (evidence.Contains("downstream", StringComparison.OrdinalIgnoreCase) || evidence.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Downstream Dependency", "Inspect downstream dependency health", "Signal evidence points to timeouts or downstream dependency behavior. Check dependency health and circuit breaker state.", 78));
        }

        if (evidence.Contains("config", StringComparison.OrdinalIgnoreCase) || evidence.Contains("secret", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Configuration", "Inspect recent configuration changes", "Signal evidence mentions configuration or secret state. Compare deployed configuration against the last healthy version.", 74));
        }

        if (HasHistoricalPostmortem(anomaly))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Known Fingerprint", "Review previous postmortem for this fingerprint", "A prior resolved incident had the same service/fingerprint pattern. Compare action items before starting a fresh investigation.", 86));
        }

        if (!_signals.Where(s => anomaly.SignalIds.Contains(s.SignalId)).Any(s => !string.IsNullOrWhiteSpace(s.TraceId)))
        {
            recommendations.Add(NewRecommendation(incident.IncidentId, "Observability Gap", "Attach trace context to future signals", "No trace ID was available for this anomaly. Add trace propagation to improve root-cause speed.", 68));
        }

        recommendations.Add(NewRecommendation(incident.IncidentId, "Observability", "Preserve trace and fingerprint context", "Keep trace ID, fingerprint, deployment ID, and timeline entries attached for postmortem quality.", 70));

        foreach (var recommendation in recommendations.GroupBy(r => $"{r.Category}:{r.Title}").Select(g => g.First()))
        {
            _recommendations.Add(recommendation);
            Publish("RecommendationGenerated", correlationId, incident.IncidentId.ToString(), "IncidentFlow.Recommendation", recommendation);
            RecordTimeline(incident.IncidentId, "RecommendationGenerated", $"Recommendation generated: {recommendation.Title}", recommendation.CreatedAt, correlationId);
        }
    }

    private static RecommendationRecord NewRecommendation(Guid incidentId, string category, string title, string detail, int confidence)
    {
        return new RecommendationRecord(Guid.NewGuid(), incidentId, category, title, detail, confidence, "Suggested", DateTimeOffset.UtcNow);
    }

    private void UpdateReports()
    {
        RecordInboxProcessed("IncidentFlow.Reporting", Guid.NewGuid());
        Publish("ReportingProjectionUpdated", Guid.NewGuid().ToString("N"), null, "IncidentFlow.Reporting", BuildReport(_incidents));
    }

    private IncidentSummaryReport BuildReport(IReadOnlyList<IncidentRecord> incidents)
    {
        var acknowledged = incidents.Where(i => i.AcknowledgedAt.HasValue).ToList();
        var resolved = incidents.Where(i => i.ResolvedAt.HasValue).ToList();
        return new IncidentSummaryReport(
            incidents.Count,
            incidents.Count(i => i.Status is not IncidentStatus.Resolved and not IncidentStatus.Closed),
            resolved.Count,
            acknowledged.Count == 0 ? 0 : acknowledged.Average(i => (i.AcknowledgedAt!.Value - i.DetectedAt).TotalMinutes),
            resolved.Count == 0 ? 0 : resolved.Average(i => (i.ResolvedAt!.Value - i.DetectedAt).TotalMinutes),
            incidents.GroupBy(i => i.Severity.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            incidents.GroupBy(i => i.AffectedService).OrderByDescending(g => g.Count()).Take(10).ToDictionary(g => g.Key, g => g.Count()),
            incidents.Where(i => i.RootCauseSummary?.Contains("Deployment", StringComparison.OrdinalIgnoreCase) == true)
                .GroupBy(i => i.AffectedService)
                .ToDictionary(g => g.Key, g => g.Count())
        );
    }

    private IEnumerable<IncidentRecord> FilterIncidents(ReportFilter? filter)
    {
        var query = _incidents.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter?.ServiceName))
        {
            query = query.Where(i => i.AffectedService.Equals(filter.ServiceName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter?.Environment))
        {
            query = query.Where(i => i.Environment.Equals(filter.Environment, StringComparison.OrdinalIgnoreCase));
        }

        return query;
    }

    private EventEnvelope Publish(string eventType, string correlationId, string? causationId, string source, object payload)
    {
        var envelope = new EventEnvelope(Guid.NewGuid(), eventType, 1, correlationId, causationId, source, DateTimeOffset.UtcNow, payload);
        _events.Add(envelope);
        _outbox.Add(new OutboxMessage(
            envelope.EventId,
            envelope.EventType,
            envelope.SchemaVersion,
            JsonSerializer.Serialize(payload, EventJsonOptions),
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OccurredAt,
            envelope.OccurredAt,
            0,
            null
        ));
        return envelope;
    }

    private void RecordInboxProcessed(string consumerName, Guid messageId)
    {
        if (_inbox.Any(i => i.MessageId == messageId && i.ConsumerName.Equals(consumerName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _inbox.Add(new InboxMessage(messageId, consumerName, now, now, "Processed", null));
    }

    private void RecordTimeline(Guid? incidentId, string entryType, string message, DateTimeOffset occurredAt, string correlationId)
    {
        _timeline.Add(new TimelineEntry(Guid.NewGuid(), incidentId, entryType, message, occurredAt, correlationId));
    }

    private IncidentRecord RequireIncident(Guid incidentId)
    {
        return _incidents.FirstOrDefault(i => i.IncidentId == incidentId)
            ?? throw new KeyNotFoundException($"Incident {incidentId} was not found.");
    }

    private void ReplaceIncident(IncidentRecord incident)
    {
        var index = _incidents.FindIndex(i => i.IncidentId == incident.IncidentId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Incident {incident.IncidentId} was not found.");
        }

        _incidents[index] = incident;
    }

    private bool SuppressDuplicate(string dedupeKey, DateTimeOffset occurredAt)
    {
        foreach (var staleKey in _signalDedupe.Where(pair => pair.Value < occurredAt.Subtract(DedupeRetention)).Select(pair => pair.Key).ToList())
        {
            _signalDedupe.Remove(staleKey);
        }

        if (_signalDedupe.ContainsKey(dedupeKey))
        {
            return true;
        }

        _signalDedupe[dedupeKey] = occurredAt;
        return false;
    }

    private void EnsureUnderSourceRateLimit(string sourceSystem, string serviceName, string environment, DateTimeOffset occurredAt)
    {
        var windowStart = occurredAt.AddMinutes(-1);
        var count = _signals.Count(s =>
            s.SourceSystem.Equals(sourceSystem, StringComparison.OrdinalIgnoreCase)
            && s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase)
            && s.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase)
            && s.OccurredAt >= windowStart
            && s.OccurredAt <= occurredAt);

        if (count >= SourceRateLimitPerMinute)
        {
            throw new InvalidOperationException($"Source rate limit exceeded for {sourceSystem}/{serviceName}/{environment}.");
        }
    }

    private bool HasHistoricalPostmortem(AnomalyRecord anomaly)
    {
        var currentFingerprints = _signals
            .Where(s => anomaly.SignalIds.Contains(s.SignalId))
            .Select(s => s.Fingerprint)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (currentFingerprints.Count == 0)
        {
            return false;
        }

        foreach (var postmortem in _postmortems)
        {
            var incident = _incidents.FirstOrDefault(i => i.IncidentId == postmortem.IncidentId);
            if (incident is null || !incident.AffectedService.Equals(anomaly.ServiceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_signals.Where(s => incident.LinkedSignalIds.Contains(s.SignalId)).Any(s => s.Fingerprint is not null && currentFingerprints.Contains(s.Fingerprint)))
            {
                return true;
            }
        }

        return false;
    }

    private string EvidenceFor(AnomalyRecord anomaly)
    {
        return string.Join(" ", _signals
            .Where(s => anomaly.SignalIds.Contains(s.SignalId))
            .Select(s => $"{s.Summary} {string.Join(" ", s.Metadata.Select(pair => $"{pair.Key} {pair.Value}"))}"));
    }

    private static string ProblemKey(string serviceName, string environment, string fingerprint)
    {
        return $"{serviceName}:{environment}:{fingerprint}".ToLowerInvariant();
    }

    private static string BuildDedupeKey(
        string signalType,
        string sourceSystem,
        string serviceName,
        string environment,
        string? idempotencyKey,
        string semanticKey,
        DateTimeOffset occurredAt,
        Dictionary<string, string> metadata)
    {
        var naturalKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{semanticKey}:{MetadataHash(metadata)}:{occurredAt:yyyyMMddHHmm}"
            : $"idempotency:{idempotencyKey}";

        return $"{signalType}:{sourceSystem}:{serviceName}:{environment}:{naturalKey}".ToLowerInvariant();
    }

    private static string MetadataHash(Dictionary<string, string> metadata)
    {
        return metadata.Count == 0
            ? string.Empty
            : string.Join(";", metadata.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string StableTextKey(string value)
    {
        return string.Join("-", value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(12));
    }

    private static void ValidateCommon(string sourceSystem, string serviceName, string environment)
    {
        RequireText(sourceSystem, "Source system is required.");
        RequireText(serviceName, "Service name is required.");
        RequireText(environment, "Environment is required.");
    }

    private static void RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }
    }

    private static JsonSerializerOptions CreateEventJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
