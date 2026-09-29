# Build from the repository root:
#   docker build -f src/TransitGames.Server/Dockerfile -t transitgames-server .
# or use deploy/docker-compose.yml.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY src/TransitGames.Core/TransitGames.Core.csproj src/TransitGames.Core/
COPY src/TransitGames.Server/TransitGames.Server.csproj src/TransitGames.Server/
RUN dotnet restore src/TransitGames.Server/TransitGames.Server.csproj
COPY src/TransitGames.Core/ src/TransitGames.Core/
COPY src/TransitGames.Server/ src/TransitGames.Server/
RUN dotnet publish src/TransitGames.Server/TransitGames.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The SQLite database lives on a volume so games survive container updates.
RUN mkdir -p /data && chown app:app /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DatabasePath=/data/transitgames-server.db
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TransitGames.Server.dll"]
