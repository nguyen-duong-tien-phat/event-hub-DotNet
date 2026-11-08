resource "aws_s3_bucket" "images" {
  bucket = "eventhub-images"
}

# Browsers PUT the poster straight to the bucket (presigned URL), so the dashboard's origin must be allowed.
resource "aws_s3_bucket_cors_configuration" "images" {
  bucket = aws_s3_bucket.images.id

  cors_rule {
    allowed_origins = ["http://localhost:5173"]
    allowed_methods = ["PUT", "GET"]
    allowed_headers = ["Content-Type"]
  }
}
