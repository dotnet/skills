# Docker Deployment Guide for MCP C# Servers

## Overview

This guide provides detailed instructions for containerizing HTTP MCP servers using Docker, including multi-stage builds, optimization, and production configurations.

---

## Basic Dockerfile

### Standard Multi-Stage Build

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy source code and build
COPY . ./
RUN dotnet publish -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app .

# Configure environment
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_ENVIRONMENT=Production
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "MyMcpServer.dll"]
```

---

## Optimized Dockerfiles

### With Native AOT (Smallest Image)

```dockerfile
# Build stage with AOT
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install native dependencies for AOT
RUN apt-get update && apt-get install -y clang zlib1g-dev

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app \
    -p:PublishAot=true \
    -p:OptimizationPreference=Size

# Minimal runtime image
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS runtime
WORKDIR /app

# Copy AOT-compiled binary
COPY --from=build /app/MyMcpServer ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s \
  CMD wget -q --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["./MyMcpServer"]
```

### With Self-Contained Runtime

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app \
    --self-contained true \
    -r linux-x64 \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine
WORKDIR /app

COPY --from=build /app/MyMcpServer ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["./MyMcpServer"]
```

---

## Docker Compose

### Development Configuration

```yaml
# docker-compose.yml
version: '3.8'

services:
  mcp-server:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "3001:8080"
    environment:
      - DOTNET_ENVIRONMENT=Development
      - API_KEY=${API_KEY}
      - Logging__LogLevel__Default=Debug
    volumes:
      - ./logs:/app/logs
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
```

### Production Configuration

```yaml
# docker-compose.prod.yml
version: '3.8'

services:
  mcp-server:
    image: yourregistry.azurecr.io/mymcpserver:${VERSION:-latest}
    ports:
      - "8080:8080"
    environment:
      - DOTNET_ENVIRONMENT=Production
      - API_KEY=${API_KEY}
      - Logging__LogLevel__Default=Warning
    deploy:
      replicas: 3
      resources:
        limits:
          cpus: '0.5'
          memory: 256M
        reservations:
          cpus: '0.25'
          memory: 128M
    restart: always
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
```

---

## Building Images

### Local Build

```bash
# Build with default tag
docker build -t mymcpserver:latest .

# Build with specific version
docker build -t mymcpserver:1.0.0 .

# Build with build arguments
docker build \
  --build-arg VERSION=1.0.0 \
  --build-arg BUILD_DATE=$(date -u +"%Y-%m-%dT%H:%M:%SZ") \
  -t mymcpserver:1.0.0 .
```

### Multi-Platform Build

```bash
# Create builder for multi-platform
docker buildx create --name multiplatform --use

# Build for multiple platforms
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t yourregistry/mymcpserver:1.0.0 \
  --push .
```

---

## Running Containers

### Development

```bash
# Run with environment variables
docker run -d \
  --name mymcpserver \
  -p 3001:8080 \
  -e API_KEY=your-dev-key \
  -e DOTNET_ENVIRONMENT=Development \
  mymcpserver:latest

# View logs
docker logs -f mymcpserver

# Execute shell in container
docker exec -it mymcpserver /bin/sh
```

### Production

```bash
# Run with secrets from file
docker run -d \
  --name mymcpserver \
  -p 8080:8080 \
  --env-file .env.production \
  --restart always \
  --memory 256m \
  --cpus 0.5 \
  mymcpserver:1.0.0
```

### Using Docker Compose

```bash
# Development
docker-compose up -d

# Production
docker-compose -f docker-compose.prod.yml up -d

# View logs
docker-compose logs -f mcp-server

# Scale service
docker-compose -f docker-compose.prod.yml up -d --scale mcp-server=3
```

---

## Container Registries

### Docker Hub

```bash
# Login
docker login

# Tag and push
docker tag mymcpserver:1.0.0 yourusername/mymcpserver:1.0.0
docker push yourusername/mymcpserver:1.0.0

# Push latest tag
docker tag mymcpserver:1.0.0 yourusername/mymcpserver:latest
docker push yourusername/mymcpserver:latest
```

### Azure Container Registry

```bash
# Login to ACR
az acr login --name yourregistry

# Tag for ACR
docker tag mymcpserver:1.0.0 yourregistry.azurecr.io/mymcpserver:1.0.0

# Push to ACR
docker push yourregistry.azurecr.io/mymcpserver:1.0.0

# Enable admin user (for simple deployments)
az acr update --name yourregistry --admin-enabled true
```

### GitHub Container Registry

```bash
# Login to GHCR
echo $GITHUB_TOKEN | docker login ghcr.io -u USERNAME --password-stdin

# Tag and push
docker tag mymcpserver:1.0.0 ghcr.io/yourusername/mymcpserver:1.0.0
docker push ghcr.io/yourusername/mymcpserver:1.0.0
```

---

## Security Best Practices

### Run as Non-Root User

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Create non-root user
RUN groupadd -r mcpuser && useradd -r -g mcpuser mcpuser

WORKDIR /app
COPY --from=build /app .

# Change ownership
RUN chown -R mcpuser:mcpuser /app

# Switch to non-root user
USER mcpuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyMcpServer.dll"]
```

### Read-Only Filesystem

```bash
docker run -d \
  --name mymcpserver \
  --read-only \
  --tmpfs /tmp \
  -p 8080:8080 \
  mymcpserver:1.0.0
```

### Security Scanning

```bash
# Scan with Trivy
docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  aquasec/trivy image mymcpserver:1.0.0

# Scan with Docker Scout
docker scout cves mymcpserver:1.0.0
```

---

## Health Checks

### In Application Code

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddCheck("api", () => 
        HealthCheckResult.Healthy("API is running"))
    .AddCheck("dependencies", () =>
    {
        // Check external dependencies
        return HealthCheckResult.Healthy();
    });

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMcp();

app.Run();
```

### In Dockerfile

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
```

### In Docker Compose

```yaml
services:
  mcp-server:
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
```

---

## Logging

### Configure Logging for Containers

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "json";
});
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});
```

### View Container Logs

```bash
# Stream logs
docker logs -f mymcpserver

# Last 100 lines
docker logs --tail 100 mymcpserver

# With timestamps
docker logs -t mymcpserver

# Since specific time
docker logs --since 2024-01-01T00:00:00 mymcpserver
```

---

## Troubleshooting

### Container Won't Start

```bash
# Check container status
docker ps -a

# View startup logs
docker logs mymcpserver

# Run interactively to debug
docker run -it --rm mymcpserver:latest /bin/sh
```

### Health Check Failing

```bash
# Test health endpoint manually
docker exec mymcpserver curl -f http://localhost:8080/health

# Check health check logs
docker inspect --format='{{json .State.Health}}' mymcpserver | jq
```

### Memory Issues

```bash
# Check container stats
docker stats mymcpserver

# Increase memory limit
docker run -d --memory 512m mymcpserver:latest
```

---

## Quality Checklist

### Dockerfile
- [ ] Multi-stage build used
- [ ] Minimal base image selected
- [ ] Non-root user configured
- [ ] Health check defined
- [ ] Appropriate labels added

### Security
- [ ] No secrets in Dockerfile
- [ ] Base image regularly updated
- [ ] Security scanning enabled
- [ ] Read-only filesystem where possible

### Production
- [ ] Resource limits set
- [ ] Restart policy configured
- [ ] Logging properly configured
- [ ] Health checks working
