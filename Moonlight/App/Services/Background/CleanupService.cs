﻿using Logging.Net;
using Microsoft.EntityFrameworkCore;
using MineStatLib;
using Moonlight.App.ApiClients.Daemon.Resources;
using Moonlight.App.ApiClients.Shards.Resources;
using Moonlight.App.ApiClients.Wings;
using Moonlight.App.Database.Entities;
using Moonlight.App.Events;
using Moonlight.App.Repositories;
using Moonlight.App.Repositories.Servers;
using Moonlight.App.Services.Shards;
using Newtonsoft.Json;

namespace Moonlight.App.Services.Background;

public class CleanupService
{
    #region Stats
    public DateTime StartedAt { get; private set; }
    public DateTime CompletedAt { get; private set; }
    public int ServersCleaned { get; private set; }
    public int CleanupsPerformed { get; private set; }
    public int ServersRunning { get; private set; }
    public bool IsRunning { get; private set; }
    #endregion
    
    private readonly ConfigService ConfigService;
    private readonly DateTimeService DateTimeService;
    private readonly EventSystem Event;
    private readonly IServiceScopeFactory ServiceScopeFactory;
    private readonly PeriodicTimer Timer;

    public CleanupService(
        ConfigService configService,
        IServiceScopeFactory serviceScopeFactory,
        DateTimeService dateTimeService,
        EventSystem eventSystem)
    {
        ServiceScopeFactory = serviceScopeFactory;
        DateTimeService = dateTimeService;
        ConfigService = configService;
        Event = eventSystem;
        
        StartedAt = DateTimeService.GetCurrent();
        CompletedAt = DateTimeService.GetCurrent();
        IsRunning = false;
        
        var config = ConfigService.GetSection("Moonlight").GetSection("Cleanup");

        if (!config.GetValue<bool>("Enable") || ConfigService.DebugMode)
        {
            Logger.Info("Disabling cleanup service");
            return;
        }
        
        Timer = new(TimeSpan.FromMinutes(config.GetValue<int>("Wait")));

        Task.Run(Run);
    }

    private async Task Run()
    {
        while (await Timer.WaitForNextTickAsync())
        {
            IsRunning = true;
            
            using var scope = ServiceScopeFactory.CreateScope();
            var config = ConfigService.GetSection("Moonlight").GetSection("Cleanup");

            var maxCpu = config.GetValue<int>("Cpu");
            var minMemory = config.GetValue<int>("Memory");
            var maxUptime = config.GetValue<int>("Uptime");
            var minUptime = config.GetValue<int>("MinUptime");

            var shardRepository = scope.ServiceProvider.GetRequiredService<Repository<Shard>>();
            var shardService = scope.ServiceProvider.GetRequiredService<ShardService>();

            var shards = shardRepository
                .Get()
                .ToArray();

            foreach (var shard in shards)
            {
                try
                {
                    var cpuMetrics = await shardService.GetCpuMetrics(shard);
                    var memoryMetrics = await shardService.GetMemoryMetrics(shard);

                    if (cpuMetrics.CpuUsage > maxCpu || (memoryMetrics.Total - memoryMetrics.Used) < minMemory)
                    {
                        var dockerMetrics = await shardService.GetDockerMetrics(shard);

                        var serverRepository = scope.ServiceProvider.GetRequiredService<ServerRepository>();
                        var imageRepository = scope.ServiceProvider.GetRequiredService<ImageRepository>();

                        var images = imageRepository
                            .Get()
                            .ToArray();

                        var imagesWithFlag = images
                            .Where(x => 
                                (JsonConvert.DeserializeObject<string[]>(x.TagsJson) ?? Array.Empty<string>()).Contains("cleanup")
                            )
                            .ToArray();
                        
                        var containerMappedToServers = new Dictionary<Container, Server>();

                        foreach (var container in dockerMetrics.Containers)
                        {
                            if (Guid.TryParse(container.Name, out Guid uuid))
                            {
                                var server = serverRepository
                                    .Get()
                                    .Include(x => x.Image)
                                    .Include(x => x.MainAllocation)
                                    .Include(x => x.Variables)
                                    .FirstOrDefault(x => x.Uuid == uuid);

                                if (server != null && imagesWithFlag.Any(y => y.Id == server.Image.Id))
                                {
                                    containerMappedToServers.Add(container, server);
                                }
                            }
                        }

                        var serverService = scope.ServiceProvider.GetRequiredService<ServerService>();
                        
                        foreach (var containerMapped in containerMappedToServers)
                        {
                            var server = containerMapped.Value;
                            
                            try
                            {
                                var stats = await serverService.GetDetails(server);

                                if (server.IsCleanupException)
                                {
                                    if (stats.Utilization.Uptime > TimeSpan.FromHours(maxUptime).TotalMilliseconds)
                                    {
                                        var players = GetPlayers(shard, server.MainAllocation!);

                                        if (players == 0)
                                        {
                                            await serverService.SetPowerState(server, PowerSignal.Restart);
                                            
                                            ServersCleaned++;
                                        }
                                        else
                                        {
                                            ServersRunning++;
                                        }
                                        
                                        await Event.Emit("cleanup.updated");
                                    }
                                }
                                else
                                {
                                    if (stats.Utilization.Uptime > TimeSpan.FromMinutes(minUptime).TotalMilliseconds)
                                    {
                                        var players = GetPlayers(shard, server.MainAllocation!);

                                        if (players < 1)
                                        {
                                            var j2SVar = server.Variables.FirstOrDefault(x => x.Key == "J2S");
                                            var handleJ2S = j2SVar != null && j2SVar.Value == "1";

                                            if (handleJ2S)
                                            {
                                                await serverService.SetPowerState(server, PowerSignal.Restart);
                                            }
                                            else
                                            {
                                                await serverService.SetPowerState(server, PowerSignal.Stop);
                                            }

                                            ServersCleaned++;
                                        }
                                        else
                                        {
                                            ServersRunning++;
                                        }
                                        
                                        await Event.Emit("cleanup.updated");
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.Warn($"Error checking server {server.Name} ({server.Id})");
                                Logger.Warn(e);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Error performing cleanup on node {shard.Name} ({shard.Id})");
                    Logger.Error(e);
                }
            }

            IsRunning = false;
            CleanupsPerformed++;
            await Event.Emit("cleanup.updated");
        }
    }

    private int GetPlayers(Shard shard, ShardAllocation allocation)
    {
        var ms = new MineStat(shard.Fqdn, (ushort)allocation.Port);
        
        //TODO: Add fake player check

        if (ms.ServerUp)
        {
            return ms.CurrentPlayersInt;
        }
        
        return -1;
    }
}