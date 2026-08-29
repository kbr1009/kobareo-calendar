FROM node:24-alpine AS client-build
WORKDIR /src
COPY src/KobareoCalendar.WebApp/ClientApp/package.json src/KobareoCalendar.WebApp/ClientApp/package-lock.json ./
RUN npm ci
COPY src/KobareoCalendar.WebApp/ClientApp/ ./
RUN npm run test -- --run && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY KobareoCalendar.slnx ./
COPY src/KobareoCalendar.Domain/KobareoCalendar.Domain.csproj src/KobareoCalendar.Domain/
COPY src/KobareoCalendar.Application/KobareoCalendar.Application.csproj src/KobareoCalendar.Application/
COPY src/KobareoCalendar.Infrastructure/KobareoCalendar.Infrastructure.csproj src/KobareoCalendar.Infrastructure/
COPY src/KobareoCalendar.SqlServerMigrations/KobareoCalendar.SqlServerMigrations.csproj src/KobareoCalendar.SqlServerMigrations/
COPY src/KobareoCalendar.Migration/KobareoCalendar.Migration.csproj src/KobareoCalendar.Migration/
COPY src/KobareoCalendar.WebApp/KobareoCalendar.WebApp.csproj src/KobareoCalendar.WebApp/
COPY tests/KobareoCalendar.Domain.Tests/KobareoCalendar.Domain.Tests.csproj tests/KobareoCalendar.Domain.Tests/
COPY tests/KobareoCalendar.WebApp.Tests/KobareoCalendar.WebApp.Tests.csproj tests/KobareoCalendar.WebApp.Tests/
RUN dotnet restore KobareoCalendar.slnx
COPY src/ src/
COPY tests/ tests/
COPY --from=client-build /src/dist src/KobareoCalendar.WebApp/wwwroot
RUN dotnet test KobareoCalendar.slnx -c Release --no-restore -m:1
RUN dotnet publish src/KobareoCalendar.WebApp/KobareoCalendar.WebApp.csproj -c Release -o /app --no-restore /p:UseAppHost=false
RUN dotnet publish src/KobareoCalendar.Migration/KobareoCalendar.Migration.csproj -c Release -o /migration --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app .
USER root
RUN mkdir -p /data && chown app:app /data
USER app
ENV PORT=8080 ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=5 CMD curl --fail --silent http://localhost:${PORT}/api/health || exit 1
ENTRYPOINT ["dotnet", "KobareoCalendar.WebApp.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migration
WORKDIR /app
COPY --from=build --chown=app:app /migration .
USER app
ENV DOTNET_EnableDiagnostics=0 DATABASE_PROVIDER=SqlServer
ENTRYPOINT ["dotnet", "KobareoCalendar.Migration.dll"]
