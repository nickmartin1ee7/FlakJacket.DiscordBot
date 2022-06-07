using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FlakJacket.DiscordBot.WorkerService.Services;

public class HeartbeatService : IDisposable
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly TimeSpan _delayTime = TimeSpan.FromSeconds(1);
    private CancellationTokenSource? _cts;
    private Task _job;

    public HeartbeatService(ILogger<HeartbeatService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_cts is not null && !_cts.IsCancellationRequested)
            return;

        // Must be cancelled and not running to spawn another job
        _cts = new CancellationTokenSource();
        _job = Task.Run(HeartbeatJob);
    }

    private async Task HeartbeatJob()
    {
        while (!_cts!.IsCancellationRequested)
        {
            _logger.LogTrace("Heartbeat! Next heartbeat in {_delayTime} @ {nextUpdate}", _delayTime, DateTime.Now.Add(_delayTime));
            await Task.Delay(_delayTime);
        }

        _logger.LogInformation("{serviceName} has been cancelled", nameof(HeartbeatService));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _job.Dispose();
    }
}