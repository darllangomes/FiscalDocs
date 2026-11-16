FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FiscalDocs.Domain/FiscalDocs.Domain.csproj src/FiscalDocs.Domain/
COPY src/FiscalDocs.Application/FiscalDocs.Application.csproj src/FiscalDocs.Application/
COPY src/FiscalDocs.Infrastructure/FiscalDocs.Infrastructure.csproj src/FiscalDocs.Infrastructure/
COPY src/FiscalDocs.Worker/FiscalDocs.Worker.csproj src/FiscalDocs.Worker/
RUN dotnet restore src/FiscalDocs.Worker/FiscalDocs.Worker.csproj

COPY src/ src/
RUN dotnet publish src/FiscalDocs.Worker/FiscalDocs.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app .

USER app
ENTRYPOINT ["dotnet", "FiscalDocs.Worker.dll"]
