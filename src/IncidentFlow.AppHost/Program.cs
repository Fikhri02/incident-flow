var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var redis = builder.AddRedis("redis")
    .WithRedisCommander();

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var ingestionDb = postgres.AddDatabase("ingestion-db");
var incidentDb = postgres.AddDatabase("incident-db");
var timelineDb = postgres.AddDatabase("timeline-db");
var reportingDb = postgres.AddDatabase("reporting-db");

var ingestion = builder.AddProject<Projects.IncidentFlow_Services_Ingestion_Api>("ingestion")
    .WithReference(ingestionDb)
    .WithReference(rabbit);

var incident = builder.AddProject<Projects.IncidentFlow_Services_Incident_Api>("incident")
    .WithReference(incidentDb)
    .WithReference(rabbit);

builder.AddProject<Projects.IncidentFlow_Services_Detection_Worker>("detection")
    .WithReference(rabbit)
    .WithReference(redis);

builder.AddProject<Projects.IncidentFlow_Services_Correlation_Worker>("correlation")
    .WithReference(rabbit)
    .WithReference(redis);

var timeline = builder.AddProject<Projects.IncidentFlow_Services_Timeline_Api>("timeline")
    .WithReference(timelineDb)
    .WithReference(rabbit);

// Reporting reads Timeline's stored events only for a rebuild.
var reporting = builder.AddProject<Projects.IncidentFlow_Services_Reporting_Api>("reporting")
    .WithReference(reportingDb)
    .WithReference(rabbit)
    .WithReference(timeline);

builder.AddProject<Projects.IncidentFlow_Services_Notification_Worker>("notification")
    .WithReference(rabbit)
    .WithReference(redis);

builder.AddProject<Projects.IncidentFlow_ApiGateway>("api-gateway")
    .WithReference(ingestion)
    .WithReference(incident)
    .WithReference(timeline)
    .WithReference(reporting);

builder.Build().Run();
