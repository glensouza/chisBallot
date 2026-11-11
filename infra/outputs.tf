output "static_web_app_name" {
  value = azurerm_static_web_app.this.name
}

output "admin_static_web_app_name" {
  value = azurerm_static_web_app.admin.name
}

output "static_web_app_default_host_name" {
  value = azurerm_static_web_app.this.default_host_name
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "turnstile_site_key" {
  value = cloudflare_turnstile_widget.vote.sitekey
}

# Used by the schema migration to create the two database users.
output "sql_server_admin_connection_string" {
  value     = "${local.sql_connection_base};User ID=${azurerm_mssql_server.this.administrator_login};Password=${random_password.sql_admin.result}"
  sensitive = true
}

output "sql_vote_password" {
  value     = random_password.sql_vote.result
  sensitive = true
}

output "sql_admin_app_password" {
  value     = random_password.sql_admin_app.result
  sensitive = true
}
