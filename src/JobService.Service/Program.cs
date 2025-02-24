using System;
using System.Reflection;
using System.Threading.Tasks;
using JobService.Components;
using JobService.Service;
using JobService.Service.Components;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NSwag;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("MassTransit", LogEventLevel.Debug)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();
builder.Services.AddOpenApiDocument(cfg => cfg.PostProcess = d =>
{
    d.Info.Title = "Job Consumer Sample";
    d.Info.Contact = new OpenApiContact
    {
        Name = "Job Consumer Sample using MassTransit",
        Email = "support@masstransit.io"
    };
});

// for Postgres
//AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

//builder.Services.AddDbContext<JobServiceSagaDbContext>(optionsBuilder =>
//{
//    var connectionString = builder.Configuration.GetConnectionString("JobService");

//    optionsBuilder.UseNpgsql(connectionString, m =>
//    {
//        m.MigrationsAssembly(Assembly.GetExecutingAssembly().GetName().Name);
//        m.MigrationsHistoryTable($"__{nameof(JobServiceSagaDbContext)}");
//    });
//});

//builder.Services.AddHostedService<MigrationHostedService<JobServiceSagaDbContext>>();

builder.Services.AddMassTransit(x =>
{
    x.AddDelayedMessageScheduler();

    // if message keeps getting sent to skipped queue, 1. because consumer is not available, 2. because the queue is corrupted, give it a new name.
    x.AddConsumer<ConvertVideoJobConsumer, ConvertVideoJobConsumerDefinition>()
        .Endpoint(e => e.Name = "convert-job-queue");

    x.AddConsumer<TrackVideoConvertedConsumer>();

    // for Postgres
    //x.AddSagaRepository<JobSaga>()
    //    .EntityFrameworkRepository(r =>
    //    {
    //        r.ExistingDbContext<JobServiceSagaDbContext>();
    //        r.UsePostgres();
    //    });
    //x.AddSagaRepository<JobTypeSaga>()
    //    .EntityFrameworkRepository(r =>
    //    {
    //        r.ExistingDbContext<JobServiceSagaDbContext>();
    //        r.UsePostgres();
    //    });
    //x.AddSagaRepository<JobAttemptSaga>()
    //    .EntityFrameworkRepository(r =>
    //    {
    //        r.ExistingDbContext<JobServiceSagaDbContext>();
    //        r.UsePostgres();
    //    });

    // or use in memory
    x.AddSagaRepository<JobSaga>().InMemoryRepository();
    x.AddSagaRepository<JobTypeSaga>().InMemoryRepository();
    x.AddSagaRepository<JobAttemptSaga>().InMemoryRepository();

    x.SetKebabCaseEndpointNameFormatter();

    x.UsingRabbitMq((context, cfg) =>
    {
        // configure RabbitMQ host
        if (true)
        {
            var host = "internal.test.com";
            ushort port = 5672;
            var virtualHost = "My_Host"; // use "/" or customised virtual host
            var username = "User123"; // username and password in RabbitMQ -> Admin -> Users
            var password = "User123";
            cfg.Host(host, port, virtualHost, hst =>
            {
                hst.Username(username);
                hst.Password(password);
            });
        }

        cfg.UseDelayedMessageScheduler();

        var options = new ServiceInstanceOptions()
            .SetEndpointNameFormatter(context.GetService<IEndpointNameFormatter>() ?? KebabCaseEndpointNameFormatter.Instance);

        cfg.ServiceInstance(options, instance =>
        {
            instance.ConfigureJobServiceEndpoints(js =>
            {
                js.SagaPartitionCount = 1;
                js.FinalizeCompleted = false; // for demo purposes, to get state

                js.ConfigureSagaRepositories(context);
            });

            // configure the job consumer on the job service endpoints
            instance.ConfigureEndpoints(context, f => f.Include<ConvertVideoJobConsumer>());
        });

        // Configure the remaining consumers
        cfg.ReceiveEndpoint("Normal-Queue", e =>
        {
            e.ConfigureConsumer<TrackVideoConvertedConsumer>(context, config => { });
        });
    });
});

builder.Services.AddHostedService<JobSubmissionService>(); // submit messages automatically

builder.Services.AddOptions<MassTransitHostOptions>()
    .Configure(options =>
    {
        options.WaitUntilStarted = true;
        options.StartTimeout = TimeSpan.FromMinutes(1);
        options.StopTimeout = TimeSpan.FromMinutes(1);
    });

builder.Services.AddOptions<HostOptions>()
    .Configure(options => options.ShutdownTimeout = TimeSpan.FromMinutes(1));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseOpenApi();
app.UseSwaggerUi3();

app.UseRouting();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    static Task HealthCheckResponseWriter(HttpContext context, HealthReport result)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(result.ToJsonString());
    }

    endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter
    });

    endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter });

    endpoints.MapControllers();
});
;

await app.RunAsync();