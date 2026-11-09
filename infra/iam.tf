# What the API may do. S3FileStorage only signs PUT URLs and deletes posters; it never reads
# objects (browsers load images straight from the bucket), so GetObject is deliberately absent.
data "aws_iam_policy_document" "api_permissions" {
  statement {
    sid       = "PostersWriteAndDelete"
    actions   = ["s3:PutObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.images.arn}/*"]
  }

  statement {
    sid       = "ReadAppSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.app.arn]
  }

  statement {
    sid       = "EmailQueue"
    actions   = ["sqs:GetQueueUrl", "sqs:SendMessage", "sqs:ReceiveMessage", "sqs:DeleteMessage"]
    resources = [aws_sqs_queue.email.arn]
  }
}

# Who may become this role: ECS tasks (lesson 5). A role is a hat; the trust policy says who can wear it.
data "aws_iam_policy_document" "ecs_tasks_trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "api" {
  name               = "eventhub-api"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_trust.json
}

resource "aws_iam_policy" "api" {
  name   = "eventhub-api"
  policy = data.aws_iam_policy_document.api_permissions.json
}

resource "aws_iam_role_policy_attachment" "api" {
  role       = aws_iam_role.api.name
  policy_arn = aws_iam_policy.api.arn
}
