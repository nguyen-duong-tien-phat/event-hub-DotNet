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
# A cancelled booking whose Stripe payment could not be stopped or refunded: PaymentRetryWorker tries again.
resource "aws_sqs_queue" "payment_retry_dlq" {
  name = "eventhub-payment-retry-dlq"
}

resource "aws_sqs_queue" "payment_retry" {
  name                       = "eventhub-payment-retry"
  visibility_timeout_seconds = 60

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.payment_retry_dlq.arn
    maxReceiveCount     = 5
  })
}

# A poster that could not be deleted from S3: FileDeleteWorker tries again.
resource "aws_sqs_queue" "file_delete_dlq" {
  name = "eventhub-file-delete-dlq"
}

resource "aws_sqs_queue" "file_delete" {
  name                       = "eventhub-file-delete"
  visibility_timeout_seconds = 60

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.file_delete_dlq.arn
    maxReceiveCount     = 5
  })
}
