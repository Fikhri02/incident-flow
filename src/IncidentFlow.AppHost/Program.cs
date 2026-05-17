var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.IncidentFlow_ApiGateway>("api-gateway");

builder.Build().Run();
