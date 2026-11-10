FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src

COPY ["src/GeoPulse.Aggregator/GeoPulse.Aggregator.csproj", "src/GeoPulse.Aggregator/"]
COPY ["src/GeoPulse.ClickHouse/GeoPulse.ClickHouse.csproj", "src/GeoPulse.ClickHouse/"]
RUN dotnet restore "src/GeoPulse.Aggregator/GeoPulse.Aggregator.csproj"

COPY . .
RUN dotnet build "src/GeoPulse.Aggregator/GeoPulse.Aggregator.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "src/GeoPulse.Aggregator/GeoPulse.Aggregator.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoPulse.Aggregator.dll"]
