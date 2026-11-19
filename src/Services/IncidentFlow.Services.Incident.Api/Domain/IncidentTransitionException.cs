namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>Raised when a lifecycle change is not allowed from the incident's current status.</summary>
public sealed class IncidentTransitionException(string message) : InvalidOperationException(message);
