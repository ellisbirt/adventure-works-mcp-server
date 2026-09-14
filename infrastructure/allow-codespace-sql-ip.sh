#!/usr/bin/env bash
set -euo pipefail

if ! command -v az >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1; then
  exit 0
fi

if ! az account show --output none >/dev/null 2>&1; then
  echo "Azure CLI is not logged in; skipping Codespace SQL firewall update."
  exit 0
fi

resource_group_name="${AZURE_RESOURCE_GROUP:-rg-enterprise-ai-portfolio-prod}"
sql_server_name="${SQL_SERVER_NAME:-sql-enterprise-ai-srv-pw-demo}"
computer_name="$(hostname | tr -cd '[:alnum:]_-')"
firewall_rule_name="${CODESPACE_SQL_FIREWALL_RULE_NAME:-$computer_name}"
codespace_ip="$(curl --fail --silent --show-error --ipv4 --max-time 10 https://api.ipify.org)"

if [[ -z "$firewall_rule_name" ]]; then
  echo "Could not determine a valid computer name; skipping SQL firewall update."
  exit 0
fi

if [[ ! "$codespace_ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
  echo "Could not determine a valid Codespace IPv4 address; skipping SQL firewall update."
  exit 0
fi

if ! az sql server show \
  --resource-group "$resource_group_name" \
  --name "$sql_server_name" \
  --output none >/dev/null 2>&1; then
  echo "Azure SQL server not found or inaccessible; skipping SQL firewall update."
  exit 0
fi

az sql server firewall-rule create \
  --resource-group "$resource_group_name" \
  --server "$sql_server_name" \
  --name "$firewall_rule_name" \
  --start-ip-address "$codespace_ip" \
  --end-ip-address "$codespace_ip" \
  --output none

if [[ "$firewall_rule_name" != "Codespace" ]] && az sql server firewall-rule show \
  --resource-group "$resource_group_name" \
  --server "$sql_server_name" \
  --name "Codespace" \
  --output none >/dev/null 2>&1; then
  az sql server firewall-rule delete \
    --resource-group "$resource_group_name" \
    --server "$sql_server_name" \
    --name "Codespace" \
    --yes \
    --output none
fi

echo "Azure SQL firewall rule '$firewall_rule_name' allows Codespace IP $codespace_ip."