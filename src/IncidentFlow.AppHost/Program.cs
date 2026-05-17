var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var redis = builder.AddRedis("redis")
    .WithRedisCommander();

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var ingestionDb = postgres.AddDatabase("ingestion-db");

var ingestion = builder.AddProject<Projects.IncidentFlow_Services_Ingestion_Api>("ingestion")
    .WithReference(ingestionDb)
    .WithReference(rabbit);

builder.AddProject<Projects.IncidentFlow_Services_Detection_Worker>("detection")
    .WithReference(rabbit);

builder.AddProject<Projects.IncidentFlow_ApiGateway>("api-gateway")
    .WithReference(ingestion);

builder.Build().Run();
