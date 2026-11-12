#!/usr/bin/env bash
# One-time setup, run locally by a subscription owner:
#   az login && gh auth login && ./infra/bootstrap/bootstrap.sh
#
# Creates the resource group, the Terraform state storage, and an Entra app
# that GitHub Actions signs in as through OIDC (no stored Azure secret).
set -euo pipefail

REPO="${REPO:-glensouza/chisBallot}"
LOCATION="${LOCATION:-westus2}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-chisballot}"
APP_NAME="${APP_NAME:-chisballot-github}"

SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"
STATE_ACCOUNT="${STATE_ACCOUNT:-stchisballot$(echo -n "$SUBSCRIPTION_ID" | sha1sum | cut -c1-8)}"

echo "Resource group and state storage"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none
az storage account create \
  --name "$STATE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --location "$LOCATION" \
  --sku Standard_LRS --min-tls-version TLS1_2 \
  --allow-blob-public-access false --allow-shared-key-access false --output none
az storage account blob-service-properties update \
  --account-name "$STATE_ACCOUNT" --resource-group "$RESOURCE_GROUP" \
  --enable-versioning true --output none
az storage container create \
  --name tfstate --account-name "$STATE_ACCOUNT" --auth-mode login --output none || true

echo "Entra app for GitHub Actions"
CLIENT_ID="$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)"
if [[ -z "$CLIENT_ID" ]]; then
  CLIENT_ID="$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)"
  az ad sp create --id "$CLIENT_ID" --output none
fi

add_credential() {
  local name="$1" subject="$2"
  local body="{
      \"name\": \"$name\",
      \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"$subject\",
      \"audiences\": [\"api://AzureADTokenExchange\"]
    }"
  if az ad app federated-credential list --id "$CLIENT_ID" --query "[?name=='$name']" -o tsv | grep -q .; then
    az ad app federated-credential update --id "$CLIENT_ID" --federated-credential-id "$name" --parameters "$body" --output none
  else
    az ad app federated-credential create --id "$CLIENT_ID" --parameters "$body" --output none
  fi
}
# GitHub may send the repository as an immutable id-based subject
# (repo:owner@id/name@id) instead of repo:owner/name; use whatever it sends.
SUB_PREFIX="$(gh api "repos/${REPO}/actions/oidc/customization/sub" --jq '.sub_claim_prefix // empty' 2>/dev/null || true)"
SUB_PREFIX="${SUB_PREFIX:-repo:${REPO}}"
add_credential "pull-request" "${SUB_PREFIX}:pull_request"
add_credential "production" "${SUB_PREFIX}:environment:production"

echo "Role assignments (scoped to the resource group only)"
RG_ID="$(az group show --name "$RESOURCE_GROUP" --query id -o tsv)"
STATE_ID="$(az storage account show --name "$STATE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --query id -o tsv)"
az role assignment create --assignee "$CLIENT_ID" --role "Contributor" --scope "$RG_ID" --output none
az role assignment create --assignee "$CLIENT_ID" --role "Storage Blob Data Contributor" --scope "$STATE_ID" --output none

echo "GitHub repository variables"
gh variable set AZURE_CLIENT_ID --repo "$REPO" --body "$CLIENT_ID"
gh variable set AZURE_TENANT_ID --repo "$REPO" --body "$TENANT_ID"
gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$SUBSCRIPTION_ID"
gh variable set AZURE_RESOURCE_GROUP --repo "$REPO" --body "$RESOURCE_GROUP"
gh variable set TF_STATE_ACCOUNT --repo "$REPO" --body "$STATE_ACCOUNT"

cat <<NEXT

Done. Still to do by hand:
  gh variable set CLOUDFLARE_ZONE_ID    --repo $REPO --body <zone id>
  gh variable set CLOUDFLARE_ACCOUNT_ID --repo $REPO --body <account id>
  gh secret   set CLOUDFLARE_API_TOKEN  --repo $REPO
  Create a "production" environment in the repository settings and add
  yourself as a required reviewer so every apply waits for approval.
NEXT
