# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar archivos de proyecto primero (para aprovechar caché de capas)
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

# Railway asigna $PORT dinámicamente — ASP.NET lo respeta via ASPNETCORE_URLS
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MarketplaceERP.API.dll"]
