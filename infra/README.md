# Infrastructure

Everything chisBallot runs on is declared here and applied by GitHub Actions.
Nothing is created by hand in a portal, so anyone can read exactly what exists.

| Piece | Service | Cost |
|---|---|---|
| Voting site and API (chisballot.org) | Azure Static Web Apps, free plan | $0 |
| Admin site and API (admin.chisballot.org) | Azure Static Web Apps, free plan | $0 |
| Database | Azure SQL Database, free offer | $0 |
| DNS and bot check | Cloudflare DNS and Turnstile | $0 |
| Terraform state | Azure Storage (a few kilobytes) | cents |

## One-time setup

1. Run `./infra/bootstrap/bootstrap.sh` while signed in to `az` and `gh`.
2. Create a Cloudflare API token limited to this zone with
   **Zone / DNS / Edit** and **Account / Turnstile / Edit**, then set the
   variables and secret the script prints.
3. Create the `production` environment in the repository settings with a
   required reviewer.

## Day to day

- Open a pull request that touches `infra/`: the workflow posts the plan.
- Merge to `main`: the workflow applies it after approval.

## Notes

- The first apply may need a second run: Azure validates the apex domain
  only after the TXT record exists. If validation stays pending, compare the
  TXT host Azure shows for the custom domain with `apex_txt_record_name`.
- The free database pauses after an hour idle. The first request afterwards
  takes up to a minute, so open the election a few minutes early.
- The database password lives only in Terraform state and the app settings.
  State access is limited to the deploy identity.
