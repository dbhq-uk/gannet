# Build and run gannet's web host. Central package management means the whole repository is
# copied so restore sees Directory.Packages.props and Directory.Build.props.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Gannet.Web/Gannet.Web.csproj -c Release -o /app/web \
 && dotnet publish src/Gannet.Cli/Gannet.Cli.csproj -c Release -o /app/cli

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/web .
COPY --from=build /app/cli /app/cli
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gannet.Web.dll"]
