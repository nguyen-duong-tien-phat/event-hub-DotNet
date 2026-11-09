using EventHub.BackgroundJobs;
using EventHub.Core.Email;
using EventHub.Infrastructure.Email;

namespace EventHub.Startup;

/// <summary>
/// Wires up email. Three IEmailSender registrations, told apart by key:
/// "raw" sends for real (SMTP, or the log), "deliver" is "raw" that sends each email key only once,
/// and the unkeyed one is what the services ask for: it queues the email on SQS, or delivers directly.
/// </summary>
public static class EmailSetup {
    public static async Task AddEmailAsync(this WebApplicationBuilder builder) {
        var config = builder.Configuration;
        var services = builder.Services;

        // What the app calls to send an email: one method per email (see EmailService).
        services.AddScoped<EmailService>();

        // "raw": SMTP, or the log when no mail server is configured.
        var smtpHost = config["Smtp:Host"];
        if (!string.IsNullOrEmpty(smtpHost)) {
            services.AddKeyedSingleton<IEmailSender>("raw", new SmtpEmailSender(
                smtpHost,
                int.Parse(config["Smtp:Port"] ?? "25"),
                config["Smtp:From"] ?? "Event Hub <no-reply@eventhub.local>",
                config["Smtp:User"],
                config["Smtp:Password"]));
        }
        else {
            services.AddKeyedSingleton<IEmailSender, LogEmailSender>("raw");
        }

        // "deliver": "raw", but each email key only once.
        services.AddScoped<ISentEmailRepository, SentEmailRepository>();
        services.AddKeyedScoped<IEmailSender>("deliver", (sp, _) => new DeduplicatingEmailSender(
            sp.GetRequiredService<ISentEmailRepository>(),
            sp.GetRequiredKeyedService<IEmailSender>("raw")));

        // Unkeyed: with Sqs:EmailQueue set, sending only queues the email and EmailQueueWorker delivers it.
        var emailQueue = config["Sqs:EmailQueue"];
        if (string.IsNullOrEmpty(emailQueue)) {
            services.AddScoped(sp => sp.GetRequiredKeyedService<IEmailSender>("deliver"));
            return;
        }

        var sqs = SqsSetup.CreateClient(config);
        var queueUrl = (await sqs.GetQueueUrlAsync(emailQueue)).QueueUrl;   // fails at startup if the queue is missing

        services.AddSingleton<IEmailSender>(_ => new SqsEmailSender(sqs, queueUrl));
        services.AddHostedService(sp => new EmailQueueWorker(sqs, queueUrl,
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<EmailQueueWorker>>()));
    }
}
