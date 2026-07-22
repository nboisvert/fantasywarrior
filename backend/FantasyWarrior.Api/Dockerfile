# Build from the repo root: docker build -f backend/FantasyWarrior.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/FantasyWarrior.Core/FantasyWarrior.Core.csproj backend/FantasyWarrior.Core/
COPY backend/FantasyWarrior.Api/FantasyWarrior.Api.csproj backend/FantasyWarrior.Api/
RUN dotnet restore backend/FantasyWarrior.Api/FantasyWarrior.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/FantasyWarrior.Api/FantasyWarrior.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Cloud Run routes traffic to 8080 by default; keep the container port fixed
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FantasyWarrior.Api.dll"]
