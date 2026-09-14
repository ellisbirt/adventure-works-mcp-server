#!/usr/bin/env bash
set -euo pipefail

: "${KEY_VAULT_NAME:?Set KEY_VAULT_NAME to the Terraform key_vault_name output}"
: "${ANTHROPIC_API_KEY:?Set ANTHROPIC_API_KEY in the environment}"

az keyvault secret set \
  --vault-name "$KEY_VAULT_NAME" \
  --name "anthropic-api-key" \
  --value "$ANTHROPIC_API_KEY" \
  --output none

unset ANTHROPIC_API_KEY
echo "Anthropic API secret stored in Key Vault: $KEY_VAULT_NAME/anthropic-api-key"