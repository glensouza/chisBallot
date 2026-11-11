# ---------------------------------------------------------------------------
# Cloudflare: DNS for the domain and the Turnstile widget on the ballot page
# ---------------------------------------------------------------------------

# Records are DNS-only (not proxied) so GitHub and Azure can validate the
# domains and issue their own certificates.

# Landing page on GitHub Pages. www is the Pages custom domain; the apex has
# the Pages addresses so GitHub redirects chisballot.org to www.chisballot.org.
locals {
  github_pages_ipv4 = ["185.199.108.153", "185.199.109.153", "185.199.110.153", "185.199.111.153"]
  github_pages_ipv6 = ["2606:50c0:8000::153", "2606:50c0:8001::153", "2606:50c0:8002::153", "2606:50c0:8003::153"]
}

resource "cloudflare_dns_record" "apex_a" {
  for_each = toset(local.github_pages_ipv4)

  zone_id = var.cloudflare_zone_id
  name    = var.domain_name
  type    = "A"
  content = each.value
  ttl     = 1
  proxied = false
}

resource "cloudflare_dns_record" "apex_aaaa" {
  for_each = toset(local.github_pages_ipv6)

  zone_id = var.cloudflare_zone_id
  name    = var.domain_name
  type    = "AAAA"
  content = each.value
  ttl     = 1
  proxied = false
}

resource "cloudflare_dns_record" "www" {
  zone_id = var.cloudflare_zone_id
  name    = "www.${var.domain_name}"
  type    = "CNAME"
  content = var.github_pages_host
  ttl     = 1
  proxied = false
}

# Voting site: vote.chisballot.org
resource "cloudflare_dns_record" "vote" {
  zone_id = var.cloudflare_zone_id
  name    = "vote.${var.domain_name}"
  type    = "CNAME"
  content = azurerm_static_web_app.this.default_host_name
  ttl     = 1
  proxied = false
}

resource "azurerm_static_web_app_custom_domain" "vote" {
  static_web_app_id = azurerm_static_web_app.this.id
  domain_name       = "vote.${var.domain_name}"
  validation_type   = "cname-delegation"

  depends_on = [cloudflare_dns_record.vote]
}

# Admin site: admin.chisballot.org
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
  domains    = ["vote.${var.domain_name}"]
  mode       = "managed"
}
