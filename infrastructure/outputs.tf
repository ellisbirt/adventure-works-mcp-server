output "gateway_app_url" {
  value       = "https://${azurerm_container_app.gateway.ingress[0].fqdn}"
  description = "Public HTTPS URL for the deployed Container App."
}

output "gateway_app_name" {
  value       = azurerm_container_app.gateway.name
  description = "Name of the gateway Container App."
}

output "gateway_managed_identity_principal_id" {
  value       = azurerm_user_assigned_identity.gateway.principal_id
  description = "Principal ID of the gateway user-assigned managed identity."
}

output "key_vault_name" {
  value       = azurerm_key_vault.gateway.name
  description = "Name of the Key Vault containing gateway secrets."
}

output "key_vault_uri" {
  value       = azurerm_key_vault.gateway.vault_uri
  description = "URI of the Key Vault containing gateway secrets."
}

output "anthropic_secret_name" {
  value       = var.anthropic_secret_name
  description = "Out-of-band Key Vault secret name used by the gateway."
}

output "database_principal_name" {
  value       = var.database_principal_name
  description = "Microsoft Entra database principal that must be granted db_datareader."
}

output "application_insights_connection_string" {
  value       = azurerm_application_insights.gateway.connection_string
  sensitive   = true
  description = "Application Insights connection string for application configuration."
}
