FROM mcr.microsoft.com/dotnet/sdk:10.0.100@sha256:c7445f141c04f1a6b454181bd098dcfa606c61ba0bd213d0a702489e5bd4cd71 AS build
WORKDIR /src
COPY ["Gateway.csproj", "."]
RUN dotnet restore "Gateway.csproj"
COPY ["AssemblyInfo.cs", "."]
COPY ["Program.cs", "."]
COPY ["appsettings.json", "."]
COPY ["Core", "./Core"]
COPY ["Data", "./Data"]
COPY ["Infrastructure", "./Infrastructure"]
COPY ["Integration", "./Integration"]
COPY ["Logging", "./Logging"]
COPY ["Properties", "./Properties"]
RUN dotnet publish "Gateway.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled@sha256:9651fa59abcdf177c30392cb44a820605ca5d618429ab37acbf6e7c644510b02 AS runtime
WORKDIR /app
ARG VCS_REF=unknown
ARG BUILD_DATE=unknown
ARG VERSION=unknown
LABEL org.opencontainers.image.title="adventure-works-mcp-server" \
      org.opencontainers.image.description="Adventure Works MCP gateway" \
      org.opencontainers.image.source="https://github.com/ellisbirt/adventure-works-mcp-server" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.version="${VERSION}"
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_EnableDiagnostics_IPC=0 \
    DOTNET_EnableDiagnostics_Debugger=0 \
    DOTNET_EnableDiagnostics_Profiler=0 \
    COMPlus_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build --chown=app:app /app/publish .
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "Gateway.dll", "--container-healthcheck"]
USER app
ENTRYPOINT ["dotnet", "Gateway.dll"]