# infrastructure/main.tf

# 1. Configure the Azure Provider
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

data "azurerm_client_config" "current" {}

# 2. Resource Group Definition
resource "azurerm_resource_group" "gateway_rg" {
  name     = var.resource_group_name
  location = var.location

  tags = var.tags
}

# This is intentionally a public sample-template deployment. Private endpoints,
# VNet integration, private DNS, and ingress restrictions are omitted because
# they add fixed networking cost and complicate direct access from Codespaces
# and local VS Code. For real data, add a VNet,
# private endpoints, private DNS, and a restricted ingress gateway.

# 3. Azure SQL Logical Server
resource "azurerm_mssql_server" "sql_server" {
  name                = var.sql_server_name
  resource_group_name = azurerm_resource_group.gateway_rg.name
  location            = azurerm_resource_group.gateway_rg.location
  version             = "12.0"
  azuread_administrator {
    login_username              = var.entra_admin_login_username
    object_id                   = var.entra_admin_object_id
    tenant_id                   = data.azurerm_client_config.current.tenant_id
    azuread_authentication_only = true
  }

  # PUBLIC SAMPLE TEMPLATE DATABASE ONLY. Public access is intentional because
  # this low-cost App Service sample has no VNet or private endpoints and must
  # be reachable from Codespaces and local VS Code.
  # For real data, add private networking and disable public network access.
  public_network_access_enabled = var.enable_public_network_access
}

# Allow only the explicitly configured developer address when public SQL access
# is enabled. Leave developer_ip_address null for hosted-only deployments.
resource "azurerm_mssql_firewall_rule" "developer_access" {
  for_each         = var.enable_public_network_access && var.developer_ip_address != null ? { developer = var.developer_ip_address } : {}
  name             = "AllowDeveloper"
  server_id        = azurerm_mssql_server.sql_server.id
  start_ip_address = each.value
  end_ip_address   = each.value
}

# 5. Cost-Shielded Serverless Database Pre-Seeded with Enterprise Data
resource "azurerm_mssql_database" "adventureworks_db" {
  name        = "db-adventureworks-prod"
  server_id   = azurerm_mssql_server.sql_server.id
  collation   = "SQL_Latin1_General_CP1_CI_AS"
  max_size_gb = 32

  # Budget Shield Configuration: Serverless Gen5 Compute Tier
  sku_name                    = "GP_S_Gen5_1"
  min_capacity                = 0.5 # Min vCore allocation to minimise active runtime token spend
  auto_pause_delay_in_minutes = 60  # Auto-shutdown compute resources to £0.00 after 1 hour of zero traffic

  # The Magic Populating Switch: Instructs Azure to pre-load the Microsoft enterprise schema
  sample_name = "AdventureWorksLT"

  tags = {
    DeploymentProfile = "PayAsYouGo-Safe"
    DataGroundingTier = "Relational-MCP-Source"
  }
}

# 6. Secret store for the external Anthropic credential.
resource "azurerm_key_vault" "gateway" {
  name                       = var.key_vault_name
  location                   = azurerm_resource_group.gateway_rg.location
  resource_group_name        = azurerm_resource_group.gateway_rg.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 30
  purge_protection_enabled   = true
  rbac_authorization_enabled = true
  # PUBLIC SAMPLE TEMPLATE ONLY: the free App Service has no VNet, so Key Vault
  # must be public to support scale-to-zero hosting and local development.
  # For real secrets, use a private endpoint/private DNS and disable this.
  public_network_access_enabled = var.enable_public_network_access

  tags = var.tags
}

# 7. Application Insights and centralized logs
resource "azurerm_log_analytics_workspace" "gateway" {
  name                = "log-enterprise-ai-gateway-prod"
  location            = azurerm_resource_group.gateway_rg.location
  resource_group_name = azurerm_resource_group.gateway_rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = var.tags
}

resource "azurerm_application_insights" "gateway" {
  name                = "appi-enterprise-ai-gateway-prod"
  location            = azurerm_resource_group.gateway_rg.location
  resource_group_name = azurerm_resource_group.gateway_rg.name
  application_type    = "web"
  workspace_id        = azurerm_log_analytics_workspace.gateway.id
  retention_in_days   = 30

  tags = var.tags
}

# 8. Low-cost frontend hosting using Azure Blob Static Website.
# This is cheaper than App Service and sufficient for the built Vite assets.
# For production applications requiring WAF, custom domains, or edge caching,
# place Front Door/CDN in front of this endpoint.
resource "azurerm_storage_account" "frontend" {
  name                          = var.frontend_storage_account_name
  resource_group_name           = azurerm_resource_group.gateway_rg.name
  location                      = azurerm_resource_group.gateway_rg.location
  account_tier                  = "Standard"
  account_replication_type      = "LRS"
  min_tls_version               = "TLS1_2"
  https_traffic_only_enabled    = true
  public_network_access_enabled = true

  tags = var.tags
}

resource "azurerm_storage_account_static_website" "frontend" {
  storage_account_id = azurerm_storage_account.frontend.id
  index_document     = "index.html"
  error_404_document = "index.html"
}

# 8. Consumption Azure Container Apps hosting
resource "azurerm_user_assigned_identity" "gateway" {
  name                = "id-enterprise-ai-gateway-prod"
  location            = azurerm_resource_group.gateway_rg.location
  resource_group_name = azurerm_resource_group.gateway_rg.name

  tags = var.tags
}

resource "azurerm_role_assignment" "gateway_key_vault_secrets_user" {
  scope                = azurerm_key_vault.gateway.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.gateway.principal_id
}

resource "azurerm_container_app_environment" "gateway" {
  name                       = "cae-enterprise-ai-gateway-prod"
  location                   = azurerm_resource_group.gateway_rg.location
  resource_group_name        = azurerm_resource_group.gateway_rg.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.gateway.id

  # No VNet is attached intentionally. Consumption hosting avoids dedicated VM
  # quota and remains reachable from Codespaces/local VS Code. For real workloads,
  # use a workload-profile environment attached to a VNet and private endpoints.
  tags = var.tags
}

resource "azurerm_container_app" "gateway" {
  name                         = var.container_app_name
  container_app_environment_id = azurerm_container_app_environment.gateway.id
  resource_group_name          = azurerm_resource_group.gateway_rg.name
  revision_mode                = "Single"

  lifecycle {
    ignore_changes = [template[0].container[0].image]

    precondition {
      condition     = var.external_id_authority != "" && var.external_id_api_audience != "" && var.external_id_spa_client_id != ""
      error_message = "External ID authority, API audience, and SPA client ID must be configured before deploying the public gateway."
    }
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.gateway.id]
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 2

    container {
      name   = "gateway"
      image  = "ghcr.io/ellisbirt/adventure-works-mcp-server:release"
      cpu    = var.container_cpu
      memory = var.container_memory

      liveness_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health"
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health/ready"
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 3
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = azurerm_application_insights.gateway.connection_string
      }
      env {
        name  = "ApplicationInsights__ConnectionString"
        value = azurerm_application_insights.gateway.connection_string
      }
      env {
        name  = "Cors__AllowedOrigins__0"
        value = trimsuffix(azurerm_storage_account.frontend.primary_web_endpoint, "/")
      }
      env {
        name  = "Authentication__Enabled"
        value = "true"
      }
      env {
        name  = "Authentication__Authority"
        value = var.external_id_authority
      }
      env {
        name  = "Authentication__Audience"
        value = var.external_id_api_audience
      }
      env {
        name  = "Authentication__RequiredScope"
        value = "access_as_user"
      }
      env {
        name        = "Anthropic__ApiKey"
        secret_name = "anthropic-api-key"
      }
      env {
        name  = "Anthropic__Model"
        value = var.anthropic_model
      }
      env {
        name  = "Anthropic__MaxTokens"
        value = tostring(var.anthropic_max_tokens)
      }
      env {
        name  = "Anthropic__RequestTimeoutSeconds"
        value = tostring(var.anthropic_request_timeout_seconds)
      }
      env {
        name  = "ConnectionStrings__AdventureWorksConnection"
        value = "Server=tcp:${azurerm_mssql_server.sql_server.fully_qualified_domain_name},1433;Initial Catalog=${azurerm_mssql_database.adventureworks_db.name};Authentication=Active Directory Managed Identity;User Id=${azurerm_user_assigned_identity.gateway.client_id};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
      }
    }
  }

  secret {
    name                = "anthropic-api-key"
    key_vault_secret_id = "${azurerm_key_vault.gateway.vault_uri}secrets/${var.anthropic_secret_name}"
    identity            = azurerm_user_assigned_identity.gateway.id
  }

  tags = var.tags
}

# No azurerm_private_endpoint resources or private DNS zones are declared in
# this public sample. For a real database and secret store, add private endpoints
# here, link them to a VNet, and use private DNS zones for SQL and Key Vault.

# 9. Outputs to pass directly into the .NET application and deployment pipeline
output "sql_server_fqdn" {
  value       = azurerm_mssql_server.sql_server.fully_qualified_domain_name
  description = "The fully qualified domain name of the live Azure SQL server."
}

output "database_name" {
  value       = azurerm_mssql_database.adventureworks_db.name
  description = "The name of the database seeded with AdventureWorks."
}
