using WattsOn.Infrastructure;
using WattsOn.Infrastructure.DataHub;
using WattsOn.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Named HTTP client for Energi Data Service (spot prices)
builder.Services.AddHttpClient("EnergiDataService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(new("application/json"));
});

// DataHub B2B client (outbox dispatch)
builder.Services.Configure<DataHubSettings>(builder.Configuration.GetSection(DataHubSettings.SectionName));
builder.Services.AddHttpClient<DataHubClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(new("application/json"));
});

builder.Services.AddHostedService<DataHubInboxFetcher>();
builder.Services.AddHostedService<InboxPollingWorker>();
builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<SettlementWorker>();
builder.Services.AddHostedService<SpotPriceWorker>();

var host = builder.Build();
host.Run();
