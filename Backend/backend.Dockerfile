FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SmartExcelAnalyzerBackend.sln .
COPY API/API.csproj API/
COPY Domain/Domain.csproj Domain/
COPY Persistence/Persistence.csproj Persistence/
COPY Application/Application.csproj Application/
COPY SmartExcelAnalyzer.Tests/SmartExcelAnalyzer.Tests.csproj SmartExcelAnalyzer.Tests/

COPY . .
RUN dotnet restore SmartExcelAnalyzerBackend.sln
RUN dotnet build SmartExcelAnalyzerBackend.sln -c Release --no-restore
RUN dotnet publish API/API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5001

# .NET 8 environment variables for hosting configuration
ENV ASPNETCORE_URLS=http://+:5001
ENV ASPNETCORE_ENVIRONMENT=Development

# Disable HTTPS redirection in development
ENV ASPNETCORE_HTTP_PORTS=5001
ENV ASPNETCORE_HTTPS_PORTS=

# Explicitly configure Kestrel to use port 5001 on all interfaces
ENV Kestrel__Endpoints__Http__Url=http://0.0.0.0:5001

ENTRYPOINT ["dotnet", "API.dll"]