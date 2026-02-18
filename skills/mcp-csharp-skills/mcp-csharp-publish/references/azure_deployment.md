# Azure Deployment Guide for MCP C# Servers

## Overview

This guide provides detailed instructions for deploying HTTP MCP servers to Azure, covering Azure Container Apps (recommended), Azure App Service, and supporting services.

---

## Deployment Options Comparison

| Feature | Container Apps | App Service |
|---------|---------------|-------------|
| **Best For** | Microservices, auto-scaling | Traditional web apps |
| **Scaling** | 0 to N (scale to zero) | 1 to N (always running) |
| **Pricing** | Pay per use | Plan-based |
| **Complexity** | Low | Low-Medium |
| **Container Support** | Native | Supported |

---

## Azure Container Apps (Recommended)

### Prerequisites

```bash
# Install Azure CLI
# https://docs.microsoft.com/cli/azure/install-azure-cli

# Login to Azure
az login

# Install Container Apps extension
az extension add --name containerapp --upgrade
```

### Create Resources

```bash
# Set variables
RESOURCE_GROUP="mcp-servers-rg"
LOCATION="eastus"
ENVIRONMENT="mcp-env"
ACR_NAME="mcpserversacr"
APP_NAME="mymcpserver"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Container Registry
az acr create \
  --resource-group $RESOURCE_GROUP \
  --name $ACR_NAME \
  --sku Basic \
  --admin-enabled true

# Create Container Apps environment
az containerapp env create \
  --name $ENVIRONMENT \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION
```

### Build and Push Image

```bash
# Login to ACR
az acr login --name $ACR_NAME

# Build and push
docker build -t $ACR_NAME.azurecr.io/$APP_NAME:1.0.0 .
docker push $ACR_NAME.azurecr.io/$APP_NAME:1.0.0

# Or use ACR build (no local Docker needed)
az acr build \
  --registry $ACR_NAME \
  --image $APP_NAME:1.0.0 \
  --file Dockerfile .
```

### Deploy Container App

```bash
# Get ACR credentials
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query "passwords[0].value" -o tsv)

# Create Container App
az containerapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENVIRONMENT \
  --image $ACR_NAME.azurecr.io/$APP_NAME:1.0.0 \
  --registry-server $ACR_NAME.azurecr.io \
  --registry-username $ACR_NAME \
  --registry-password $ACR_PASSWORD \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 10 \
  --cpu 0.25 \
  --memory 0.5Gi
```

### Configure Secrets

```bash
# Create Key Vault
az keyvault create \
  --name mcp-secrets-kv \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION

# Add secret
az keyvault secret set \
  --vault-name mcp-secrets-kv \
  --name "api-key" \
  --value "your-api-key-here"

# Enable managed identity
az containerapp identity assign \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --system-assigned

# Grant Key Vault access
PRINCIPAL_ID=$(az containerapp identity show --name $APP_NAME --resource-group $RESOURCE_GROUP --query principalId -o tsv)
az keyvault set-policy \
  --name mcp-secrets-kv \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list

# Add secret reference to container app
az containerapp secret set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --secrets "api-key=keyvaultref:https://mcp-secrets-kv.vault.azure.net/secrets/api-key,identityref:/subscriptions/.../resourcegroups/.../providers/Microsoft.ManagedIdentity/userAssignedIdentities/..."

# Update environment variables
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars "API_KEY=secretref:api-key"
```

### Configure Scaling

```bash
# Scale based on HTTP requests
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --min-replicas 0 \
  --max-replicas 10 \
  --scale-rule-name "http-rule" \
  --scale-rule-type "http" \
  --scale-rule-http-concurrency 100
```

### Get Deployment URL

```bash
# Get the FQDN
az containerapp show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query properties.configuration.ingress.fqdn \
  -o tsv

# Output: mymcpserver.azurecontainerapps.io
```

---

## Azure App Service

### Create App Service Plan

```bash
# Create App Service Plan
az appservice plan create \
  --name mcp-plan \
  --resource-group $RESOURCE_GROUP \
  --is-linux \
  --sku B1

# Create Web App
az webapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --plan mcp-plan \
  --deployment-container-image-name $ACR_NAME.azurecr.io/$APP_NAME:1.0.0
```

### Configure Container Registry

```bash
# Configure ACR credentials
az webapp config container set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --docker-custom-image-name $ACR_NAME.azurecr.io/$APP_NAME:1.0.0 \
  --docker-registry-server-url https://$ACR_NAME.azurecr.io \
  --docker-registry-server-user $ACR_NAME \
  --docker-registry-server-password $ACR_PASSWORD
```

### Configure Environment Variables

```bash
# Set app settings
az webapp config appsettings set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    DOTNET_ENVIRONMENT=Production \
    API_KEY=@Microsoft.KeyVault(VaultName=mcp-secrets-kv;SecretName=api-key)

# Enable managed identity
az webapp identity assign \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP
```

### Configure Health Check

```bash
az webapp config set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --generic-configurations '{"healthCheckPath": "/health"}'
```

### Enable Continuous Deployment

```bash
# Enable CI/CD with ACR webhook
az webapp deployment container config \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --enable-cd true
```

---

## Infrastructure as Code

### Bicep Template

```bicep
// main.bicep
param location string = resourceGroup().location
param appName string = 'mymcpserver'
param acrName string = 'mcpserversacr'
param imageName string = 'mymcpserver:1.0.0'

// Container Registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// Container Apps Environment
resource env 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${appName}-env'
  location: location
  properties: {}
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: appName
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: '${acrName}.azurecr.io/${imageName}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 10
      }
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
```

### Deploy with Bicep

```bash
# Deploy
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file main.bicep \
  --parameters appName=$APP_NAME acrName=$ACR_NAME
```

---

## CI/CD with GitHub Actions

### Workflow File

```yaml
# .github/workflows/deploy-azure.yml
name: Deploy to Azure Container Apps

on:
  push:
    branches: [main]
    tags: ['v*']

env:
  AZURE_CONTAINER_REGISTRY: mcpserversacr
  CONTAINER_APP_NAME: mymcpserver
  RESOURCE_GROUP: mcp-servers-rg

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3
      
      - name: Login to Azure
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Login to ACR
        run: az acr login --name ${{ env.AZURE_CONTAINER_REGISTRY }}
      
      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          push: true
          tags: |
            ${{ env.AZURE_CONTAINER_REGISTRY }}.azurecr.io/${{ env.CONTAINER_APP_NAME }}:${{ github.sha }}
            ${{ env.AZURE_CONTAINER_REGISTRY }}.azurecr.io/${{ env.CONTAINER_APP_NAME }}:latest
          cache-from: type=gha
          cache-to: type=gha,mode=max
      
      - name: Deploy to Container Apps
        uses: azure/container-apps-deploy-action@v1
        with:
          acrName: ${{ env.AZURE_CONTAINER_REGISTRY }}
          containerAppName: ${{ env.CONTAINER_APP_NAME }}
          resourceGroup: ${{ env.RESOURCE_GROUP }}
          imageToDeploy: ${{ env.AZURE_CONTAINER_REGISTRY }}.azurecr.io/${{ env.CONTAINER_APP_NAME }}:${{ github.sha }}
```

---

## Monitoring and Logging

### Enable Application Insights

```bash
# Create Application Insights
az monitor app-insights component create \
  --app mcp-insights \
  --location $LOCATION \
  --resource-group $RESOURCE_GROUP

# Get instrumentation key
APPINSIGHTS_KEY=$(az monitor app-insights component show \
  --app mcp-insights \
  --resource-group $RESOURCE_GROUP \
  --query instrumentationKey -o tsv)

# Add to container app
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=$APPINSIGHTS_KEY"
```

### View Logs

```bash
# Stream logs
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --follow

# Query logs with Log Analytics
az monitor log-analytics query \
  --workspace $WORKSPACE_ID \
  --analytics-query "ContainerAppConsoleLogs_CL | where ContainerAppName_s == '$APP_NAME' | take 100"
```

---

## Custom Domain and SSL

### Add Custom Domain

```bash
# Add custom domain
az containerapp hostname add \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --hostname mcp.yourdomain.com

# Add managed certificate
az containerapp hostname bind \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --hostname mcp.yourdomain.com \
  --environment $ENVIRONMENT \
  --validation-method CNAME
```

---

## MCP Client Configuration

After deployment, users configure their MCP clients:

```json
{
  "servers": {
    "MyMcpServer": {
      "type": "http",
      "url": "https://mymcpserver.azurecontainerapps.io",
      "headers": {
        "Authorization": "Bearer ${input:api_key}"
      }
    }
  },
  "inputs": [
    {
      "type": "promptString",
      "id": "api_key",
      "description": "API key for authentication",
      "password": true
    }
  ]
}
```

---

## Cost Optimization

### Container Apps

- **Scale to Zero**: Set `minReplicas: 0` for dev/test environments
- **Right-size Resources**: Start with 0.25 CPU / 0.5Gi memory
- **Use Spot Instances**: For non-critical workloads

### App Service

- **Use Dev/Test Pricing**: For non-production
- **Reserved Instances**: 1-year or 3-year for production
- **Auto-scaling**: Scale down during off-hours

```bash
# Example: Scale down schedule
az monitor autoscale rule create \
  --resource-group $RESOURCE_GROUP \
  --autoscale-name my-autoscale \
  --condition "Time >= 18:00 and Time <= 08:00" \
  --scale to 1
```

---

## Quality Checklist

### Pre-Deployment
- [ ] Image builds successfully
- [ ] Health check endpoint works
- [ ] Environment variables documented
- [ ] Secrets stored in Key Vault

### Deployment
- [ ] Managed identity enabled
- [ ] Key Vault access configured
- [ ] Ingress/networking configured
- [ ] Scaling rules set

### Post-Deployment
- [ ] Health check responding
- [ ] Logs flowing to Log Analytics
- [ ] Metrics visible in Application Insights
- [ ] MCP client can connect

### Security
- [ ] HTTPS only
- [ ] No secrets in code
- [ ] Managed identity for Azure resources
- [ ] Network restrictions if needed
