resource "aws_secretsmanager_secret" "app" {
  name                    = "eventhub/app"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "app" {
  secret_id = aws_secretsmanager_secret.app.id
  secret_string = jsonencode({
    "Jwt:Key"              = var.jwt_key
    "Tickets:Secret"       = var.tickets_secret
    "Stripe:SecretKey"     = var.stripe_secret_key
    "Stripe:WebhookSecret" = var.stripe_webhook_secret
  })
}