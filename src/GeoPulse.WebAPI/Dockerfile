FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5146

ENV ASPNETCORE_URLS=http://+:5146

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["GeoPulse.WebAPI.csproj", "./"]
RUN dotnet restore "GeoPulse.WebAPI.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "GeoPulse.WebAPI.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "GeoPulse.WebAPI.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoPulse.WebAPI.dll"]
