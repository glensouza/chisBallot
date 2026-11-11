data "azurerm_resource_group" "this" {
  name = var.resource_group_name
}

resource "random_string" "suffix" {
  length  = 6
  lower   = true
  upper   = false
  numeric = true
  special = false
}

# ---------------------------------------------------------------------------
# Database: Azure SQL free offer (serverless, pauses when idle, never billed)
# ---------------------------------------------------------------------------

resource "random_password" "sql_admin" {
  length           = 32
  special          = true
  override_special = "!#%*-_"
}

resource "azurerm_mssql_server" "this" {
  name                          = "sql-chisballot-${random_string.suffix.result}"
  resource_group_name           = data.azurerm_resource_group.this.name
  location                      = var.sql_location
  version                       = "12.0"
  administrator_login           = "chisballot"
  administrator_login_password  = random_password.sql_admin.result
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true
}

# Managed Functions in a Static Web App have no fixed outbound addresses and
# cannot use a managed identity, so access is "Azure services" plus SQL login.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# The free-offer flags are set through the ARM API directly.
resource "azapi_resource" "database" {
  type      = "Microsoft.Sql/servers/databases@2023-08-01-preview"
  name      = "chisballot"
  parent_id = azurerm_mssql_server.this.id
  location  = var.sql_location

  body = {
    sku = {
      name     = "GP_S_Gen5_2"
      tier     = "GeneralPurpose"
      family   = "Gen5"
      capacity = 2
    }
    properties = {
      useFreeLimit                = true
      freeLimitExhaustionBehavior = "AutoPause"
      autoPauseDelay              = 60
      minCapacity                 = 0.5
      maxSizeBytes                = 34359738368
      collation                   = "SQL_Latin1_General_CP1_CI_AS"
    }
  }
}

# Two database users with different rights. The schema migration creates them
# with these passwords:
#   chisballot_vote   can cast a ballot and read published results, nothing else
#   chisballot_admin  can manage elections and codes, but cannot read or change ballots of an open election
resource "random_password" "sql_vote" {
  length           = 32
  special          = true
  override_special = "!#%*-_"
}

resource "random_password" "sql_admin_app" {
  length           = 32
  special          = true
  override_special = "!#%*-_"
}

locals {
  sql_connection_base = join(";", [
    "Server=tcp:${azurerm_mssql_server.this.fully_qualified_domain_name},1433",
    "Initial Catalog=${azapi_resource.database.name}",
    "Encrypt=True",
    "TrustServerCertificate=False",
    # Generous timeout: the first connection after a pause wakes the database.
    "Connection Timeout=90",
  ])
  sql_connection_vote  = "${local.sql_connection_base};User ID=chisballot_vote;Password=${random_password.sql_vote.result}"
  sql_connection_admin = "${local.sql_connection_base};User ID=chisballot_admin;Password=${random_password.sql_admin_app.result}"
}

# ---------------------------------------------------------------------------
# Apps: two separate Static Web Apps (free plan), each with its own API
#   vote   chisballot.org        public, no sign-in, no admin code or endpoints
#   admin  admin.chisballot.org  every route requires a signed-in, invited admin
# ---------------------------------------------------------------------------

resource "azurerm_static_web_app" "this" {
  name                = "swa-chisballot"
  resource_group_name = data.azurerm_resource_group.this.name
  location            = var.static_web_app_location
  sku_tier            = "Free"
  sku_size            = "Free"

  app_settings = {
    "SqlConnectionString" = local.sql_connection_vote
    "Turnstile__SiteKey"  = cloudflare_turnstile_widget.vote.sitekey
    "Turnstile__Secret"   = cloudflare_turnstile_widget.vote.secret
  }
}

resource "azurerm_static_web_app" "admin" {
  name                = "swa-chisballot-admin"
  resource_group_name = data.azurerm_resource_group.this.name
  location            = var.static_web_app_location
  sku_tier            = "Free"
  sku_size            = "Free"

  app_settings = {
    "SqlConnectionString" = local.sql_connection_admin
    "PublicSiteUrl"       = "https://${var.domain_name}"
  }
}
