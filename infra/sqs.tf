resource "aws_sqs_queue" "email_dlq" {
  name = "eventhub-email-dlq"
}

resource "aws_sqs_queue" "email" {
  name                       = "eventhub-email"
  visibility_timeout_seconds = 60

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.email_dlq.arn
    maxReceiveCount     = 5
  })
}