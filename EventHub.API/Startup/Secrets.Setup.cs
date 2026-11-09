using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace EventHub.Startup;

public static class SecretsSetup {
    /// <summary>
    /// When Aws:SecretId is set, reads the secrets from Secrets Manager. They win over .env, so call this first.
    /// shortcut: read once at startup, so a rotated secret needs a restart; use a custom IConfigurationProvider to reload.
    /// </summary>
    public static async Task AddAwsSecretsAsync(this WebApplicationBuilder builder) {
        var secretId = builder.Configuration["Aws:SecretId"];
        if (string.IsNullOrEmpty(secretId)) return;

        var serviceUrl = builder.Configuration["Aws:ServiceUrl"];
        using var secrets = string.IsNullOrEmpty(serviceUrl)
            ? new AmazonSecretsManagerClient()
            : new AmazonSecretsManagerClient(new AmazonSecretsManagerConfig {
                ServiceURL = serviceUrl,
                AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1" });
        var secret = await secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId });
        var json = secret.SecretString ?? throw new InvalidOperationException($"Secret '{secretId}' has no text value.");
        builder.Configuration.AddInMemoryCollection(JsonSerializer.Deserialize<Dictionary<string, string?>>(json)!);
    }
}
