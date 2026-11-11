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
  description = "Apex domain served by the app."
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

variable "apex_txt_record_name" {
  description = "Host name of the TXT record Azure checks to validate the apex domain. Confirm against the value the Azure portal shows for the custom domain."
  type        = string
  default     = "_dnsauth"
}
