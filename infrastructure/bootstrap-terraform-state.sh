#!/usr/bin/env bash
set -euo pipefail

subscription_id="${ARM_SUBSCRIPTION_ID:-$(az account show --query id -o tsv)}"
resource_group_name="${TF_STATE_RESOURCE_GROUP:-rg-enterprise-ai-portfolio-prod}"
storage_account_name="${TF_STATE_STORAGE_ACCOUNT:-stentaitfstateprod}"
location="${TF_STATE_LOCATION:-uksouth}"
container_name="${TF_STATE_CONTAINER:-tfstate}"

if [[ -z "$subscription_id" ]]; then
  echo "Azure subscription is required." >&2
  exit 1
fi

az account set --subscription "$subscription_id"
az group show --name "$resource_group_name" --output none

# Dedicated state account: public blob access is disabled, HTTPS/TLS 1.2 is
# required, and blob versioning/retention protects against accidental changes.
az storage account create \
  --name "$storage_account_name" \
  --resource-group "$resource_group_name" \
  --location "$location" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --https-only true \
  --allow-blob-public-access false \
  --public-network-access Enabled \
  --output none

storage_key=$(az storage account keys list \
  --account-name "$storage_account_name" \
  --resource-group "$resource_group_name" \
  --query '[0].value' \
  --output tsv)

export AZURE_STORAGE_ACCOUNT="$storage_account_name"
export AZURE_STORAGE_KEY="$storage_key"

az storage account blob-service-properties update \
  --account-name "$storage_account_name" \
  --resource-group "$resource_group_name" \
  --enable-versioning true \
  --enable-delete-retention true \
  --delete-retention-days 30 \
  --output none

state_scope=$(az storage account show \
  --name "$storage_account_name" \
  --resource-group "$resource_group_name" \
  --query id \
  --output tsv)
principal_id=$(az ad signed-in-user show --query id --output tsv)

# Azure Storage data-plane access is separate from subscription Owner access.
az role assignment create \
  --assignee-object-id "$principal_id" \
  --assignee-principal-type User \
  --role "Storage Blob Data Contributor" \
  --scope "$state_scope" \
  --output none 2>/dev/null || true

az storage container create \
  --account-name "$storage_account_name" \
  --name "$container_name" \
  --public-access off \
  --output none

echo "Terraform state storage is ready: $storage_account_name/$container_name"
echo "Next: terraform init -migrate-state"
