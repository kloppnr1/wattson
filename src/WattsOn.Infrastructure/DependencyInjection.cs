using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WattsOn.Application.Interfaces;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("WattsOn")
            ?? throw new InvalidOperationException("Connection string 'WattsOn' not found.");

        services.AddDbContext<WattsOnDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IWattsOnDbContext>(provider => provider.GetRequiredService<WattsOnDbContext>());

        return services;
    }
}
