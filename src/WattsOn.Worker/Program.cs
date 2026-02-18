using WattsOn.Infrastructure;
using WattsOn.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpClient("EnergiDataService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(new("application/json"));
});
builder.Services.AddHostedService<InboxPollingWorker>();
builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<SettlementWorker>();
builder.Services.AddHostedService<SpotPriceWorker>();

var host = builder.Build();
host.Run();
