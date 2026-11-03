FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src

# Контекст сборки — корень решения, пути ниже относительны корня.
# Копируем только csproj, чтобы слой restore кэшировался отдельно от исходников.
COPY ["src/GeoPulse.Ingestion.Service/GeoPulse.Ingestion.Service.csproj", "src/GeoPulse.Ingestion.Service/"]
COPY ["src/GeoPulse.Core/GeoPulse.Core.csproj", "src/GeoPulse.Core/"]
RUN dotnet restore "src/GeoPulse.Ingestion.Service/GeoPulse.Ingestion.Service.csproj"

COPY . .
RUN dotnet build "src/GeoPulse.Ingestion.Service/GeoPulse.Ingestion.Service.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "src/GeoPulse.Ingestion.Service/GeoPulse.Ingestion.Service.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Минимальный набор данных Volve, запекаемый в образ: производственный Excel + текстовые
# траектории WITSML (WellWellbore). Полный датасет (EDM.XML, отчёты бурения ~385 МБ)
# парсером не используется и в образ не включается. Пути в appsettings.json относительные
# и разрешаются от рабочей директории /app.
COPY ["data/Volve_Production_data", "/app/data/Volve_Production_data"]
COPY ["data/Volve_Well_technical_data/WellWellbore", "/app/data/Volve_Well_technical_data/WellWellbore"]

ENTRYPOINT ["dotnet", "GeoPulse.Ingestion.Service.dll"]
