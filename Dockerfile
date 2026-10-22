FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer so NuGet packages stay cached until the project file changes.
COPY SushiMarketplace.Api.csproj ./
RUN dotnet restore SushiMarketplace.Api.csproj

COPY . ./
RUN dotnet publish SushiMarketplace.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql loads libgssapi_krb5 when it connects, and the slim base image does not ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SushiMarketplace.Api.dll"]
