# syntax=docker/dockerfile:1
# Build multi-stage: cada estágio tem só as ferramentas de que precisa,
# e a imagem final leva apenas o runtime do ASP.NET + o app publicado (sem SDK, sem Node).

# ---------- 1. Front: gera os arquivos estáticos do React ----------
FROM node:24-alpine AS web
WORKDIR /web
# Copiar só os manifests antes do código faz o Docker reaproveitar o cache do "npm ci"
# enquanto as dependências não mudarem.
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---------- 2. Back: compila e publica a API ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Agenda.Api/Agenda.Api.csproj src/Agenda.Api/
RUN dotnet restore src/Agenda.Api/Agenda.Api.csproj
COPY src/ src/
RUN dotnet publish src/Agenda.Api/Agenda.Api.csproj -c Release -o /app --no-restore

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# O build do React vira o wwwroot: o mesmo container serve a API e o site.
COPY --from=web /web/dist ./wwwroot
# Usuário sem privilégios de root (já existe na imagem oficial).
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Agenda.Api.dll"]
