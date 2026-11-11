# ---------------------------------------------------------------------------
# Cloudflare: DNS for the domain and the Turnstile widget on the ballot page
# ---------------------------------------------------------------------------

# Records are DNS-only (not proxied) so Azure can validate the domain and
# issue its own certificate.
resource "cloudflare_dns_record" "apex" {
  zone_id = var.cloudflare_zone_id
  name    = var.domain_name
  type    = "CNAME" # flattened by Cloudflare at the apex
  content = azurerm_static_web_app.this.default_host_name
  ttl     = 1
  proxied = false
}

resource "cloudflare_dns_record" "www" {
  zone_id = var.cloudflare_zone_id
  name    = "www.${var.domain_name}"
  type    = "CNAME"
  content = azurerm_static_web_app.this.default_host_name
  ttl     = 1
  proxied = false
}

resource "azurerm_static_web_app_custom_domain" "apex" {
  static_web_app_id = azurerm_static_web_app.this.id
  domain_name       = var.domain_name
  validation_type   = "dns-txt-token"
}

resource "cloudflare_dns_record" "apex_validation" {
  zone_id = var.cloudflare_zone_id
  name    = "${var.apex_txt_record_name}.${var.domain_name}"
  type    = "TXT"
  content = "\"${azurerm_static_web_app_custom_domain.apex.validation_token}\""
  ttl     = 300
}

resource "azurerm_static_web_app_custom_domain" "www" {
  static_web_app_id = azurerm_static_web_app.this.id
  domain_name       = "www.${var.domain_name}"
  validation_type   = "cname-delegation"

  depends_on = [cloudflare_dns_record.www]
}

resource "cloudflare_dns_record" "admin" {
  zone_id = var.cloudflare_zone_id
  name    = "admin.${var.domain_name}"
  type    = "CNAME"
  content = azurerm_static_web_app.admin.default_host_name
  ttl     = 1
  proxied = false
}

resource "azurerm_static_web_app_custom_domain" "admin" {
  static_web_app_id = azurerm_static_web_app.admin.id
  domain_name       = "admin.${var.domain_name}"
  validation_type   = "cname-delegation"

  depends_on = [cloudflare_dns_record.admin]
}

resource "cloudflare_turnstile_widget" "vote" {
  account_id = var.cloudflare_account_id
  name       = "chisBallot ballot page"
  domains    = [var.domain_name, "www.${var.domain_name}"]
  mode       = "managed"
}
