# Docker and Azure Deployment

Production deployment patterns for HTTP MCP servers.

## Production Dockerfile

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer caching)
COPY *.csproj ./
RUN dotnet restore

# Build and publish
COPY . ./
RUN dotnet publish -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Non-root user (security)
RUN adduser --disabled-password --gecos '' appuser
USER appuser

# Configure
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MyMcpServer.dll"]
```

### Key Practices

- **Multi-stage build** — keeps the final image small (no SDK, only runtime)
- **Restore-first layer** — `COPY *.csproj` then `dotnet restore` before `COPY .` for better layer caching
- **Non-root user** — run as unprivileged user in production
- **Health check** — enables orchestrator health monitoring
- **Never copy secrets** — no `.env` files, no `appsettings.Production.json` with secrets

## Azure Container Registry (ACR)

```bash
# Create ACR (one-time)
az acr create --name yourregistry --resource-group mygroup --sku Basic

# Login
az acr login --name yourregistry

# Build and push
docker build -t <yourregistry>.azurecr.io/<mymcpserver>:1.0.0 .
docker push <yourregistry>.azurecr.io/<mymcpserver>:1.0.0

# Or build directly in ACR (no local Docker needed)
az acr build --registry yourregistry --image mymcpserver:1.0.0 .
```

## Azure Container Apps

Serverless container hosting with auto-scaling. Best for MCP servers that need to scale to zero when idle.

### Full Setup

```bash
# Create environment (one-time)
az containerapp env create \
  --name myenvironment \
  --resource-group mygroup \
  --location eastus

# Create the container app
az containerapp create \
  --name mymcpserver \
  --resource-group mygroup \
  --environment myenvironment \
  --image <yourregistry>.azurecr.io/<mymcpserver>:1.0.0 \
  --registry-server <yourregistry>.azurecr.io \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 10 \
  --cpu 0.5 \
  --memory 1.0Gi \
  --secrets api-key="your-secret-value" \
  --env-vars API_KEY=secretref:api-key

# Get the URL
az containerapp show --name mymcpserver --resource-group mygroup \
  --query properties.configuration.ingress.fqdn -o tsv
```

### Update Deployment

```bash
az containerapp update \
  --name mymcpserver \
  --resource-group mygroup \
  --image <yourregistry>.azurecr.io/<mymcpserver>:1.1.0
```

### Scaling Configuration

```bash
# Scale based on HTTP requests
az containerapp update \
  --name mymcpserver \
  --resource-group mygroup \
  --scale-rule-name http-rule \
  --scale-rule-type http \
  --scale-rule-http-concurrency 50
```

## Azure App Service

Traditional web hosting with more control over infrastructure.

```bash
# Create App Service plan
az appservice plan create \
  --name myplan \
  --resource-group mygroup \
  --sku B1 \
  --is-linux

# Create web app with container
az webapp create \
  --name mymcpserver \
  --resource-group mygroup \
  --plan myplan \
  --deployment-container-image-name <yourregistry>.azurecr.io/<mymcpserver>:1.0.0

# Configure secrets via Key Vault
az webapp config appsettings set \
  --name mymcpserver \
  --resource-group mygroup \
  --settings API_KEY=@Microsoft.KeyVault(VaultName=myvault;SecretName=api-key)
```

## Secrets Management

### Azure Container Apps

```bash
# Add a secret
az containerapp secret set --name mymcpserver --resource-group mygroup \
  --secrets api-key="value"

# Reference in environment variables
az containerapp update --name mymcpserver --resource-group mygroup \
  --set-env-vars API_KEY=secretref:api-key
```

### Azure Key Vault (App Service)

```bash
# Create Key Vault
az keyvault create --name myvault --resource-group mygroup

# Add secret
az keyvault secret set --vault-name myvault --name api-key --value "your-secret"

# Grant access to App Service identity
az webapp identity assign --name mymcpserver --resource-group mygroup
az keyvault set-policy --name myvault \
  --object-id <managed-identity-id> \
  --secret-permissions get list
```

### In Application Code

```csharp
// Read from environment (works with both approaches)
var apiKey = Environment.GetEnvironmentVariable("API_KEY")
    ?? throw new InvalidOperationException("API_KEY environment variable required");

// Or use Azure Key Vault directly
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{vaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```
