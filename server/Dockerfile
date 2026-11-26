# GuardHawk Server — docker build -f server/Dockerfile -t guardrail-server .   (from the repository root)
# The build runs on the machine's own platform; the output is portable .NET, so one build serves x64 and ARM64 images.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/Guardrail.Sync/Guardrail.Sync.csproj server/Guardrail.Sync/
COPY server/Guardrail.Server/Guardrail.Server.csproj server/Guardrail.Server/
RUN dotnet restore server/Guardrail.Server/Guardrail.Server.csproj
COPY server/Guardrail.Sync/ server/Guardrail.Sync/
COPY server/Guardrail.Server/ server/Guardrail.Server/
COPY src/Guardrail.Desktop/Assets/guardrail.ico src/Guardrail.Desktop/Assets/
RUN dotnet publish server/Guardrail.Server/Guardrail.Server.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
LABEL org.opencontainers.image.title="GuardHawk Server" \
      org.opencontainers.image.description="Self-hosted sync server for GuardHawk, with a web panel. One container with SQLite, or many behind a load balancer with PostgreSQL and S3 storage." \
      org.opencontainers.image.vendor="Bytehawk Studio" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.source="https://github.com/adamglassner/guardrailehs"
WORKDIR /app
COPY --from=build /app .
# Runs as the image's unprivileged "app" user; /data must be writable by it.
RUN mkdir -p /data && chown app:app /data
USER app
ENV GR_DATA=/data GR_PORT=5080
VOLUME /data
EXPOSE 5080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD ["dotnet", "GuardrailServer.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "GuardrailServer.dll"]
