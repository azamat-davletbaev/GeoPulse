FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src

# Контекст сборки — корень решения, пути ниже относительны корня.
# Копируем только csproj, чтобы слой restore кэшировался отдельно от исходников.
COPY ["src/GeoPulse.Consumer/GeoPulse.Consumer.csproj", "src/GeoPulse.Consumer/"]
COPY ["src/GeoPulse.Infrastructure/GeoPulse.Infrastructure.csproj", "src/GeoPulse.Infrastructure/"]
COPY ["src/GeoPulse.Core/GeoPulse.Core.csproj", "src/GeoPulse.Core/"]
RUN dotnet restore "src/GeoPulse.Consumer/GeoPulse.Consumer.csproj"

COPY . .
RUN dotnet build "src/GeoPulse.Consumer/GeoPulse.Consumer.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "src/GeoPulse.Consumer/GeoPulse.Consumer.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoPulse.Consumer.dll"]
