# syntax=docker/dockerfile:1.7

# ============================================================
# dev — SDK + dotnet watch (consumed by compose dais-bridge-dev)
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev
WORKDIR /src

COPY dais-bridge.csproj ./
RUN dotnet restore dais-bridge.csproj

ENV DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

CMD ["dotnet", "watch", "run", "--no-launch-profile", "--project", "dais-bridge.csproj"]

# ============================================================
# build — produce published binary
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY dais-bridge.csproj ./
RUN dotnet restore dais-bridge.csproj

COPY . ./
RUN dotnet publish dais-bridge.csproj \
    -c Release \
    -o /publish \
    --no-restore

# ============================================================
# prod — runtime image with published binary, non-root user
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS prod
WORKDIR /app

COPY --from=build /publish ./

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

USER app
ENTRYPOINT ["dotnet", "dais-bridge.dll"]
