# Builds the sites from the repository for a pull-request preview. One compile feeds
# three small images: docker build --target vote|admin|migrator.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG SHA=unknown
WORKDIR /src
COPY . .
RUN dotnet publish src/ChisBallot.Vote.Server --configuration Release -p:SourceRevisionId=$SHA --output /out/vote \
 && dotnet publish src/ChisBallot.Admin --configuration Release -p:SourceRevisionId=$SHA --output /out/admin \
 && dotnet publish src/ChisBallot.Migrator --configuration Release -p:SourceRevisionId=$SHA --output /out/migrator

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS vote
WORKDIR /app
COPY --from=build /out/vote .
ENTRYPOINT ["dotnet", "ChisBallot.Vote.Server.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS admin
WORKDIR /app
COPY --from=build /out/admin .
ENTRYPOINT ["dotnet", "ChisBallot.Admin.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator .
ENTRYPOINT ["dotnet", "ChisBallot.Migrator.dll"]
