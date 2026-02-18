using WattsOn.Infrastructure;
using WattsOn.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<InboxPollingWorker>();
builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<SettlementWorker>();

var host = builder.Build();
host.Run();
