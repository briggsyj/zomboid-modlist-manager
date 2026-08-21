# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ModlistManager/ModlistManager.csproj ModlistManager/
RUN dotnet restore ModlistManager/ModlistManager.csproj

COPY src/ModlistManager/ ModlistManager/
RUN dotnet publish ModlistManager/ModlistManager.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# SteamCMD is x86/i386-only, so this image must run as linux/amd64 (see docker-compose.yml).
# Installed from Valve's tarball (not the apt package) to avoid an interactive EULA prompt.
RUN dpkg --add-architecture i386 \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl lib32gcc-s1 lib32stdc++6 \
    && mkdir -p /opt/steamcmd \
    && curl -sSL "https://media.steampowered.com/installer/steamcmd_linux.tar.gz" | tar -xz -C /opt/steamcmd \
    && apt-get purge -y curl \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

ENV SteamCmd__ExecutablePath=/opt/steamcmd/steamcmd.sh \
    SteamCmd__WorkshopContentRoot=/opt/steamcmd \
    ConnectionStrings__Default="Data Source=/data/modlist.db" \
    DataProtection__KeyPath=/data/keys \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

RUN mkdir -p /data
VOLUME ["/data"]

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "ModlistManager.dll"]
