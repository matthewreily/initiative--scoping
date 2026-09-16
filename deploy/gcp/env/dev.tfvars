project_id          = "initiative-scoping-dev"
region              = "us-central1"
environment         = "dev"
db_tier             = "db-f1-micro"
min_instances       = 0
max_instances       = 2
seed_reference_data = true
entra_tenant_id     = "f0f37d2f-1252-4242-8058-8b307b86b0b5"
entra_client_id     = "488767c9-e55d-441f-962c-816cbc1f40fc"
alert_emails        = ["me@mattreily.com"]
bootstrap_admins    = ["me@mattreily.com"]
# prod promotes the exact image that ran in dev, so its deployer may read this registry.
image_pull_members = ["serviceAccount:initiative-scoping-prod-deploy@initiative-scoping-prod.iam.gserviceaccount.com"]
billing_account    = "00EDAE-09A2AC-75569D"
monthly_budget_usd = 25
graph_mail_sender  = "me@mattreily.com"
