# syntax=docker/dockerfile:1
# Multi-stage build: each stage only has the tools it needs,
# and the final image ships just the ASP.NET runtime + the published app (no SDK, no Node).

# ---------- 1. Front end: builds React's static files ----------
FROM node:24-alpine AS web
WORKDIR /web
# Copying only the manifests before the code lets Docker reuse the "npm ci" cache
# as long as dependencies don't change.
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---------- 2. Back end: compiles and publishes the API ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Agenda.Api/Agenda.Api.csproj src/Agenda.Api/
RUN dotnet restore src/Agenda.Api/Agenda.Api.csproj
COPY src/ src/
RUN dotnet publish src/Agenda.Api/Agenda.Api.csproj -c Release -o /app --no-restore

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Links the image in GitHub Container Registry (ghcr.io) to the repository page.
LABEL org.opencontainers.image.source="https://github.com/c4ldeira27/Juntos"
WORKDIR /app
COPY --from=build /app ./
# The React build becomes wwwroot: the same container serves the API and the site.
COPY --from=web /web/dist ./wwwroot
# Non-root user (already present in the official image).
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Agenda.Api.dll"]
