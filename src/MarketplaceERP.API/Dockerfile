# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar archivos de proyecto primero (para aprovechar caché de capas)
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/MarketplaceERP.Shared/MarketplaceERP.Shared.csproj", "src/MarketplaceERP.Shared/"]
COPY ["src/MarketplaceERP.Core/MarketplaceERP.Core.csproj", "src/MarketplaceERP.Core/"]
COPY ["src/MarketplaceERP.Catalog/MarketplaceERP.Catalog.csproj", "src/MarketplaceERP.Catalog/"]
COPY ["src/MarketplaceERP.Finance/MarketplaceERP.Finance.csproj", "src/MarketplaceERP.Finance/"]
COPY ["src/MarketplaceERP.API/MarketplaceERP.API.csproj", "src/MarketplaceERP.API/"]

# Restaurar dependencias (capa separada para caché)
RUN dotnet restore "src/MarketplaceERP.API/MarketplaceERP.API.csproj"

# Copiar el resto del código fuente
COPY . .

# Publicar en modo Release
WORKDIR "/src/src/MarketplaceERP.API"
RUN dotnet publish "MarketplaceERP.API.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Crear usuario no-root por seguridad
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

# Copiar artefactos publicados
COPY --from=build /app/publish .

# Puerto local predeterminado; Program.cs aplica PORT en tiempo de ejecución.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MarketplaceERP.API.dll"]
