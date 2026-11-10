# syntax=docker/dockerfile:1

# ---------- Build stage: restore, build and publish with the .NET SDK ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only the project file, so the restore layer is cached
# until the dependencies change.
COPY GameNest_BackEnd.csproj ./
RUN dotnet restore GameNest_BackEnd.csproj

# Copy the rest of the sources (filtered by .dockerignore) and build.
COPY . ./
RUN dotnet build GameNest_BackEnd.csproj -c Release --no-restore

RUN dotnet publish GameNest_BackEnd.csproj -c Release --no-build -o /app/publish /p:UseAppHost=false

# ---------- Runtime stage: ASP.NET Core runtime only, no SDK ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Production unless overridden by the hosting platform.
# ASPNETCORE_HTTP_PORTS is the default port inside the container (all interfaces).
# When the platform provides PORT (Render does), Program.cs binds to 0.0.0.0:$PORT instead.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=true \
    ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish ./

# Run as the non-root "app" user that ships with the official .NET images.
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "GameNest_BackEnd.dll"]
