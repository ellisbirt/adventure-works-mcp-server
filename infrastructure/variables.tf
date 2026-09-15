variable "subscription_id" {
  description = "Azure subscription ID used by Terraform."
  type        = string
}

variable "location" {
  description = "Azure region for the gateway resources."
  type        = string
  default     = "UK South"
}

variable "resource_group_name" {
  description = "Resource group containing the gateway infrastructure."
  type        = string
  default     = "rg-enterprise-ai-portfolio-prod"
}

variable "sql_server_name" {
  description = "Globally unique Azure SQL logical server name."
  type        = string
  default     = "sql-enterprise-ai-srv-pw-demo"
}

variable "entra_admin_login_username" {
  description = "Microsoft Entra administrator login name for Azure SQL, for example the operator's live.com account."
  type        = string
}

variable "entra_admin_object_id" {
  description = "Object ID of the Microsoft Entra administrator assigned to Azure SQL."
  type        = string
}

variable "database_principal_name" {
  description = "Microsoft Entra service principal name created in SQL for the gateway managed identity."
  type        = string
  default     = "id-enterprise-ai-gateway-prod"
}

variable "developer_ip_address" {
  description = "Optional single public IPv4 address for local VS Code or Codespace SQL access. Leave null to disable developer access."
  type        = string
  default     = null
  nullable    = true
}

variable "enable_public_network_access" {
  description = "Enable public access to Azure SQL and Key Vault. Required by this low-cost sample because no private networking is provisioned. Disable for real data when private endpoints are added."
  type        = bool
  default     = true
}

variable "container_app_name" {
  description = "Globally unique Azure Container App name for the gateway."
  type        = string
  default     = "aca-enterprise-ai-gateway-prod"
}

variable "gateway_container_image" {
  description = "Container image for the gateway, published to a public or authenticated registry."
  type        = string
}

variable "container_cpu" {
  description = "Container Apps vCPU allocation."
  type        = number
  default     = 0.25
}

variable "container_memory" {
  description = "Container Apps memory allocation."
  type        = string
  default     = "0.5Gi"
}

variable "key_vault_name" {
  description = "Globally unique Azure Key Vault name for gateway secrets."
  type        = string
  default     = "kv-ent-ai-gw-prod"
}

variable "frontend_storage_account_name" {
  description = "Globally unique storage account name for the React static website."
  type        = string
  default     = "stentaigatewayweb"
}

variable "anthropic_secret_name" {
  description = "Name of the Anthropic API secret created out-of-band in Key Vault."
  type        = string
  default     = "anthropic-api-key"
}

variable "anthropic_model" {
  description = "Anthropic model used by the gateway."
  type        = string
  default     = "claude-3-5-sonnet-20241022"
}

variable "anthropic_max_tokens" {
  description = "Maximum Anthropic response tokens."
  type        = number
  default     = 1024
}

variable "anthropic_request_timeout_seconds" {
  description = "Anthropic request timeout in seconds."
  type        = number
  default     = 30
}

variable "external_id_authority" {
  description = "Microsoft Entra External ID OpenID Connect authority for the protected gateway API. Leave empty to allow local unauthenticated development."
  type        = string
  default     = ""
}

variable "external_id_api_audience" {
  description = "Application ID URI or audience configured for the gateway API. Leave empty to allow local unauthenticated development."
  type        = string
  default     = ""
}

variable "external_id_spa_client_id" {
  description = "Public client ID of the External ID SPA application."
  type        = string
  default     = ""
}

variable "tags" {
  description = "Tags applied to gateway resources."
  type        = map(string)
  default = {
    ArchitectureLayer = "Governance-Mesh"
    CostCenter        = "Interview-Sandbox"
    ManagedBy         = "Terraform"
  }
}
