namespace JobService.Components
{
    using System;
    using System.Threading.Tasks;
    using MassTransit;
    using Microsoft.Extensions.Logging;


    public class ConvertVideoJobConsumer :
        IJobConsumer<ConvertVideo>
    {
        readonly ILogger<ConvertVideoJobConsumer> _logger;
        private readonly ISendEndpointProvider _sendEndpointProvider;

        public ConvertVideoJobConsumer(ISendEndpointProvider sendEndpointProvider, ILogger<ConvertVideoJobConsumer> logger)
        {
            _sendEndpointProvider = sendEndpointProvider;
            _logger = logger;
        }

        public async Task Run(JobContext<ConvertVideo> context)
        {
            var rng = new Random();

            var variance = TimeSpan.FromMilliseconds(rng.Next(8399, 28377));

            _logger.LogInformation("Converting Video: {GroupId} {Path}", context.Job.GroupId, context.Job.Path);

            var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:Normal-Queue"));
            await endpoint.Send(new VideoConverted()
            {
                Count = context.Job.Count,
                GroupId = context.Job.GroupId,
                Index = context.Job.Index
            });

            _logger.LogInformation("Converted Video: {GroupId} {Path}", context.Job.GroupId, context.Job.Path);
        }
    }
}