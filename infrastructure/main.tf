# infrastructure/main.tf

# 1. Configure the Azure Provider
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

# 2. Resource Group Definition
resource "azurerm_resource_group" "gateway_rg" {
  name     = "rg-enterprise-ai-portfolio-prod"
  location = "UK South"
  
  tags = {
    ArchitectureLayer = "Governance-Mesh"
    CostCenter        = "Interview-Sandbox"
  }
}

# 3. Azure SQL Logical Server
resource "azurerm_mssql_server" "sql_server" {
  name                         = "sql-enterprise-ai-srv-pw-demo" # MUST be globally unique across Azure
  resource_group_name          = azurerm_resource_group.gateway_rg.name
  location                     = azurerm_resource_group.gateway_rg.location
  version                      = "12.0"
  administrator_login          = "gateway_admin"
  administrator_login_password = "SecurePassword2026!#" # Change this to a secure key

  # Enable Azure Services to access this database (For Azure App Service later)
  public_network_access_enabled = true
}

# 4. Safe Firewall Rule for Local/Home Development Sandbox
resource "azurerm_mssql_firewall_rule" "home_dev_access" {
  name             = "AllowHomeStaticIp"
  server_id        = azurerm_mssql_server.sql_server.id
  start_ip_address = "193.117.243.250" 
  end_ip_address   = "255.255.255.255" # Restrict this range tightly for actual enterprise production profiles
}

# 5. Cost-Shielded Serverless Database Pre-Seeded with Enterprise Data
resource "azurerm_mssql_database" "adventureworks_db" {
  name                        = "db-adventureworks-prod"
  server_id                   = azurerm_mssql_server.sql_server.id
  collation                   = "SQL_Latin1_General_CP1_CI_AS"
  license_type                = "BasePrice"
  max_size_gb                 = 32
  
  # Budget Shield Configuration: Serverless Gen5 Compute Tier
  sku_name                    = "GP_S_Gen5_1" 
  min_capacity                = 0.5            # Min vCore allocation to minimise active runtime token spend
  auto_pause_delay_in_minutes = 60             # Auto-shutdown compute resources to £0.00 after 1 hour of zero traffic

  # The Magic Populating Switch: Instructs Azure to pre-load the Microsoft enterprise schema
  sample_name                 = "AdventureWorksLT" 

  tags = {
    DeploymentProfile = "PayAsYouGo-Safe"
    DataGroundingTier = "Relational-MCP-Source"
  }
}

# 6. Outputs to Pass directly into your .NET application strings
output "sql_server_fqdn" {
  value       = azurerm_mssql_server.sql_server.fully_qualified_domain_name
  description = "The fully qualified domain name of the live Azure SQL server."
}

output "database_name" {
  value       = azurerm_mssql_database.adventureworks_db.name
  description = "The name of the database seeded with AdventureWorks."
}
