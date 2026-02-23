FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/WattsOn.Api/WattsOn.Api.csproj", "src/WattsOn.Api/"]
COPY ["src/WattsOn.Infrastructure/WattsOn.Infrastructure.csproj", "src/WattsOn.Infrastructure/"]
COPY ["src/WattsOn.Application/WattsOn.Application.csproj", "src/WattsOn.Application/"]
COPY ["src/WattsOn.Domain/WattsOn.Domain.csproj", "src/WattsOn.Domain/"]
RUN dotnet restore "src/WattsOn.Api/WattsOn.Api.csproj"
COPY . .
WORKDIR "/src/src/WattsOn.Api"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WattsOn.Api.dll"]
