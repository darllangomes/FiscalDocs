FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia primeiro só o que o restore precisa, para a camada de restore ficar em cache até um .csproj mudar.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FiscalDocs.Domain/FiscalDocs.Domain.csproj src/FiscalDocs.Domain/
COPY src/FiscalDocs.Application/FiscalDocs.Application.csproj src/FiscalDocs.Application/
COPY src/FiscalDocs.Infrastructure/FiscalDocs.Infrastructure.csproj src/FiscalDocs.Infrastructure/
COPY src/FiscalDocs.Api/FiscalDocs.Api.csproj src/FiscalDocs.Api/
RUN dotnet restore src/FiscalDocs.Api/FiscalDocs.Api.csproj

COPY src/ src/
RUN dotnet publish src/FiscalDocs.Api/FiscalDocs.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Usuário não-root que já vem nas imagens do .NET 8.
USER app
ENTRYPOINT ["dotnet", "FiscalDocs.Api.dll"]
