# --- Etapa 1: Build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia solo los .csproj primero (aprovecha la caché de Docker si no cambian las dependencias)
COPY src/TravelAgency.Api/TravelAgency.Api.csproj src/TravelAgency.Api/
COPY src/TravelAgency.Shared/TravelAgency.Shared.csproj src/TravelAgency.Shared/

RUN dotnet restore src/TravelAgency.Api/TravelAgency.Api.csproj

# Copia el resto del código fuente necesario
COPY src/TravelAgency.Api/ src/TravelAgency.Api/
COPY src/TravelAgency.Shared/ src/TravelAgency.Shared/

RUN dotnet publish src/TravelAgency.Api/TravelAgency.Api.csproj -c Release -o /app/publish --no-restore

# --- Etapa 2: Runtime (imagen final, más ligera) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Cloud Run inyecta la variable PORT dinámicamente; ASP.NET Core debe escuchar ahí
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TravelAgency.Api.dll"]
