using Amazon.S3;
using Amazon.SQS;
using EventHub.BackgroundJobs;
using EventHub.Core.Payments;
using EventHub.Infrastructure.Queues;

namespace EventHub.Startup;

public static class SqsSetup {
    /// <summary>The SQS client: Floci when Aws:ServiceUrl is set, real AWS otherwise.</summary>
    public static AmazonSQSClient CreateClient(IConfiguration config) {
        var serviceUrl = config["Aws:ServiceUrl"];
        return string.IsNullOrEmpty(serviceUrl)
            ? new AmazonSQSClient()
            : new AmazonSQSClient(new AmazonSQSConfig {
                ServiceURL = serviceUrl,
                AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"
            });
    }

    /// <summary>
    /// The retry queues. Each is optional: with its name empty nothing is queued and the failure is handled as before
    /// (a Stripe error is thrown / logged, an S3 error is logged). A name that does not exist stops the API at startup.
    /// </summary>
    public static async Task AddRetryQueuesAsync(this WebApplicationBuilder builder) {
        var config = builder.Configuration;
        var services = builder.Services;
        var paymentQueue = config["Sqs:PaymentRetryQueue"];
        var fileQueue = config["Sqs:FileDeleteQueue"];
        if (string.IsNullOrEmpty(paymentQueue) && string.IsNullOrEmpty(fileQueue)) return;

        var sqs = CreateClient(config);

        if (!string.IsNullOrEmpty(paymentQueue)) {
            var url = (await sqs.GetQueueUrlAsync(paymentQueue)).QueueUrl;
            services.AddSingleton<IPaymentRetryQueue>(new SqsPaymentRetryQueue(sqs, url));
            services.AddHostedService(sp => new PaymentRetryWorker(sqs, url,
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<PaymentRetryWorker>>()));
        }

        if (!string.IsNullOrEmpty(fileQueue)) {
            var url = (await sqs.GetQueueUrlAsync(fileQueue)).QueueUrl;
            var bucket = config["S3:Bucket"]!;
            services.AddSingleton<IFileDeleteQueue>(new SqsFileDeleteQueue(sqs, url));
            services.AddHostedService(sp => new FileDeleteWorker(sqs, url,
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<FileDeleteWorker>>(),
                sp.GetRequiredService<IAmazonS3>(), bucket));
        }
    }
}
