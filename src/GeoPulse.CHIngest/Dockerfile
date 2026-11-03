FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src

COPY ["src/GeoPulse.CHIngest/GeoPulse.CHIngest.csproj", "src/GeoPulse.CHIngest/"]
COPY ["src/GeoPulse.Core/GeoPulse.Core.csproj", "src/GeoPulse.Core/"]
RUN dotnet restore "src/GeoPulse.CHIngest/GeoPulse.CHIngest.csproj"

COPY . .
RUN dotnet build "src/GeoPulse.CHIngest/GeoPulse.CHIngest.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "src/GeoPulse.CHIngest/GeoPulse.CHIngest.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoPulse.CHIngest.dll"]
