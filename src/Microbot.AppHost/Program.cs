var builder = DistributedApplication.CreateBuilder(args);

// Add the Microbot console application as a project resource
// This allows Aspire to orchestrate and monitor the application
var microbot = builder.AddProject<Projects.Microbot_Console>("microbot")
    .WithExternalHttpEndpoints();

// Build and run the distributed application
builder.Build().Run();
