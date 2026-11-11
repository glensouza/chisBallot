variable "resource_group_name" {
  description = "Existing resource group (created by bootstrap.sh) that holds every chisBallot resource."
  type        = string
  default     = "rg-chisballot"
}

variable "static_web_app_location" {
  description = "Region for the Static Web App's managed Functions. Static Web Apps is only offered in a few regions."
  type        = string
  default     = "westus2"
}

variable "sql_location" {
  description = "Region for Azure SQL. The free offer locks every free database in the subscription to the first region used."
  type        = string
  default     = "westus2"
}

variable "domain_name" {
  description = "Apex domain. The apex and www serve the GitHub Pages landing page; vote. and admin. are the two apps."
  type        = string
  default     = "chisballot.org"
}

variable "cloudflare_zone_id" {
  description = "Cloudflare zone ID for the domain."
  type        = string
}

variable "cloudflare_account_id" {
  description = "Cloudflare account ID (owner of the Turnstile widget)."
  type        = string
}

variable "github_pages_host" {
  description = "Default GitHub Pages host of the account that owns the public repo. The www record points here; do not include the repository name."
  type        = string
  default     = "glensouza.github.io"
}
