FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5146

ENV ASPNETCORE_URLS=http://+:5146

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src

# Контекст сборки — корень решения, пути ниже относительны корня.
# Копируем только csproj, чтобы слой restore кэшировался отдельно от исходников.
COPY ["src/GeoPulse.WebAPI/GeoPulse.WebAPI.csproj", "src/GeoPulse.WebAPI/"]
COPY ["src/GeoPulse.Infrastructure/GeoPulse.Infrastructure.csproj", "src/GeoPulse.Infrastructure/"]
COPY ["src/GeoPulse.ClickHouse/GeoPulse.ClickHouse.csproj", "src/GeoPulse.ClickHouse/"]
COPY ["src/GeoPulse.Core/GeoPulse.Core.csproj", "src/GeoPulse.Core/"]
RUN dotnet restore "src/GeoPulse.WebAPI/GeoPulse.WebAPI.csproj"

COPY . .
RUN dotnet build "src/GeoPulse.WebAPI/GeoPulse.WebAPI.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "src/GeoPulse.WebAPI/GeoPulse.WebAPI.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# curl нужен healthcheck'у webapi в docker-compose.prod.yml: в образе
# aspnet:10.0 нет ни curl, ни wget. Ставим в отдельном слое до COPY, чтобы он
# кэшировался независимо от кода приложения; базовый образ работает под USER app,
# поэтому для установки пакетов временно переключаемся на root.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoPulse.WebAPI.dll"]
