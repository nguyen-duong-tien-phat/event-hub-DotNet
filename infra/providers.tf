terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

# Everything here talks to Floci (a local AWS emulator), never to real AWS:
# each service listed under `endpoints` is redirected to localhost.
provider "aws" {
  region     = "us-east-1"
  access_key = "test"
  secret_key = "test"

  # Real AWS is asked "who am I?" before anything else; Floci does not answer that.
  skip_credentials_validation = true
  skip_metadata_api_check     = true
  skip_requesting_account_id  = true

  # localhost:4566/<bucket> instead of <bucket>.localhost:4566 (no per-bucket DNS locally).
  s3_use_path_style = true

  endpoints {
    s3             = "http://localhost:4566"
    iam            = "http://localhost:4566"
    secretsmanager = "http://localhost:4566"
  }
}
