# Terraform state is stored in Azure Blob Storage with native blob locking.
# Run bootstrap-terraform-state.sh once before `terraform init -migrate-state`.
# The storage account name must match the bootstrap script default.
terraform {
  backend "azurerm" {
    resource_group_name  = "rg-enterprise-ai-portfolio-prod"
    storage_account_name = "stentaitfstateprod"
    container_name       = "tfstate"
    key                  = "enterprise-ai-gateway.tfstate"
    use_azuread_auth     = true
  }
}
