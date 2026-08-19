# stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EventHub.sln .
COPY EventHub.API/EventHub.API.csproj EventHub.API/
COPY EventHub.Core/EventHub.Core.csproj EventHub.Core/
COPY EventHub.Infrastructure/EventHub.Infrastructure.csproj EventHub.Infrastructure/

RUN dotnet restore EventHub.API/EventHub.API.csproj

COPY . .
RUN dotnet publish EventHub.API/EventHub.API.csproj -c Release -o /app/publish

# stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "EventHub.API.dll"]