terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.0"
    }
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Storage account, container and key are passed with -backend-config
  # (see infra/README.md and .github/workflows/infra.yml).
  backend "azurerm" {
    use_oidc         = true
    use_azuread_auth = true
  }
}

# Credentials come from the environment:
#   ARM_CLIENT_ID, ARM_TENANT_ID, ARM_SUBSCRIPTION_ID, ARM_USE_OIDC
#   CLOUDFLARE_API_TOKEN
provider "azurerm" {
  features {}

  # The deploy identity can act only inside its resource group, so it cannot
  # register resource providers. Microsoft.Web, Microsoft.Sql and
  # Microsoft.Storage are already registered on the subscription.
  resource_provider_registrations = "none"
}

provider "azapi" {}

provider "cloudflare" {}
