# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/SeatGuard.Domain/SeatGuard.Domain.csproj src/SeatGuard.Domain/
COPY src/SeatGuard.Application/SeatGuard.Application.csproj src/SeatGuard.Application/
COPY src/SeatGuard.Infrastructure/SeatGuard.Infrastructure.csproj src/SeatGuard.Infrastructure/
COPY src/SeatGuard.Api/SeatGuard.Api.csproj src/SeatGuard.Api/

RUN dotnet restore src/SeatGuard.Api/SeatGuard.Api.csproj

COPY src/ src/

RUN dotnet publish src/SeatGuard.Api/SeatGuard.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "SeatGuard.Api.dll"]
