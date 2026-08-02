# Build from the repo root: docker build -f backend/FantasyWarrior.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first, so a source-only change reuses the restore layer.
COPY backend/FantasyWarrior.Core/FantasyWarrior.Core.csproj backend/FantasyWarrior.Core/
COPY backend/FantasyWarrior.Data/FantasyWarrior.Data.csproj backend/FantasyWarrior.Data/
COPY backend/FantasyWarrior.Api/FantasyWarrior.Api.csproj backend/FantasyWarrior.Api/
RUN dotnet restore backend/FantasyWarrior.Api/FantasyWarrior.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/FantasyWarrior.Api/FantasyWarrior.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Container Apps routes to 8080 by default, as Cloud Run did before it.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# The connection string arrives as the AZURE_SQL_CONNECTION environment
# variable, injected from a Container Apps secret. The appsettings.json baked
# into this image holds a placeholder and is never used in production.
ENTRYPOINT ["dotnet", "FantasyWarrior.Api.dll"]
