FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/WattsOn.Worker/WattsOn.Worker.csproj", "src/WattsOn.Worker/"]
COPY ["src/WattsOn.Infrastructure/WattsOn.Infrastructure.csproj", "src/WattsOn.Infrastructure/"]
COPY ["src/WattsOn.Application/WattsOn.Application.csproj", "src/WattsOn.Application/"]
COPY ["src/WattsOn.Domain/WattsOn.Domain.csproj", "src/WattsOn.Domain/"]
RUN dotnet restore "src/WattsOn.Worker/WattsOn.Worker.csproj"
COPY . .
WORKDIR "/src/src/WattsOn.Worker"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WattsOn.Worker.dll"]
