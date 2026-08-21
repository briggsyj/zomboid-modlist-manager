# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore and publish against the full source tree in one go. A csproj-only restore step (common
# Docker layer-caching trick) breaks Static Web Assets discovery here: razor-component-colocated
# JS and even blazor.web.js itself end up silently missing from the publish output, which kills
# every interactive feature client-side with no server-side error at all.
COPY src/ModlistManager/ ModlistManager/
RUN dotnet publish ModlistManager/ModlistManager.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Mod IDs are read from the Steam Workshop API over HTTPS, so the trust store must be present.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# SteamCMD is an optional extra, off by default: it only ships a 32-bit x86 binary, so it cannot
# execute on an arm64 host emulating amd64 (Docker on Apple Silicon), and it downloads an entire
# mod just to read one mod.info file. Build with --build-arg INSTALL_STEAMCMD=true and run with
# SteamCmd__Enabled=true on a real amd64 host to use it as the authoritative Mod ID source.
ARG INSTALL_STEAMCMD=false
RUN if [ "$INSTALL_STEAMCMD" = "true" ]; then \
        dpkg --add-architecture i386 \
        && apt-get update \
        && apt-get install -y --no-install-recommends curl lib32gcc-s1 lib32stdc++6 \
        && mkdir -p /opt/steamcmd \
        && curl -sSL "https://media.steampowered.com/installer/steamcmd_linux.tar.gz" | tar -xz -C /opt/steamcmd \
        && apt-get purge -y curl \
        && apt-get autoremove -y \
        && rm -rf /var/lib/apt/lists/*; \
    fi

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
