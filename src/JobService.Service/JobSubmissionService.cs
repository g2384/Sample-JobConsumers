namespace JobService.Service;

using System;
using System.Threading;
using System.Threading.Tasks;
using JobService.Components;
using MassTransit;
using MassTransit.Contracts.JobService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class JobSubmissionService : BackgroundService
{
    readonly IBusControl _bus;
    readonly ILogger<JobSubmissionService> _logger;

    public JobSubmissionService(ILogger<JobSubmissionService> logger, IBusControl bus)
    {
        _logger = logger;
        _bus = bus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.WaitForHealthStatus(BusHealthStatus.Healthy, stoppingToken);

        IRequestClient<ConvertVideo> client = _bus.CreateRequestClient<ConvertVideo>();

        await Task.Delay(1000); // waiting for the queue to setup

        const int total = 2;

        // send via client
        for (var i = 0; i < total; i++)
        {
            await Task.Delay(400, stoppingToken);

            var groupId = NewId.Next().ToString();

            var path = NewId.Next() + ".txt";

            Response<JobSubmissionAccepted> response = await client.GetResponse<JobSubmissionAccepted>(new
            {
                path,
                groupId,
                Index = i + 1,
                Count = total
            });

            _logger.LogInformation("Job submitted: {Content} {Index}/{Count}", response.Message, i + 1, total);
        }

        await Task.Delay(1000); // waiting for previous jobs to finish
        _logger.LogInformation("===============================================================");

        // send via URI
        var sender = await _bus.GetSendEndpoint(new Uri("queue:convert-job-queue"));
        for (var i = 0; i < total; i++)
        {
            await Task.Delay(400, stoppingToken);

            var groupId = NewId.Next().ToString();

            var message = new ConvertVideo
            {
                Path = NewId.Next() + ".txt",
                GroupId = groupId,
                Index = i + 1,
                Count = total
            };

            await sender.Send(message, new CancellationToken());

            _logger.LogInformation("Job submitted: {Content} {Index}/{Count}", message.Path, i + 1, total);
        }
        return;
    }
}