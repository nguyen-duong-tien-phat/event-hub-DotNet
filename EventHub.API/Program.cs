using DotNetEnv;
using EventHub.Startup;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

Env.Load("../.env");

var builder = WebApplication.CreateBuilder(args);

// What each call registers is in EventHub.API/Startup.
await builder.AddAwsSecretsAsync();   // first: the secrets must be in the configuration before anything reads them
builder.AddDatabase();
builder.AddRedis();
builder.AddPayments();
builder.AddAuth();
await builder.AddEmailAsync();
await builder.AddRetryQueuesAsync();
builder.AddFileStorage();
builder.AddAppServices();
builder.AddWebDefaults();

var app = builder.Build();

// Configure the HTTP request pipeline.
// Logging sits outside the exception handler so a failed request is logged with its 500.
// (In Development the developer exception page runs first and shows the details instead.)
app.UseForwardedHeaders(); // first, so everything below sees the client's address and scheme
// Probes hit /health every few seconds; they would drown the request log.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpLogging());
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// On in Development, and wherever Swagger:Enabled is true (the compose stack sets it; a container runs as Production).
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled")) {
    app.UseSwagger();
    app.UseSwaggerUI(c => c.EnablePersistAuthorization());
}

app.Run();
