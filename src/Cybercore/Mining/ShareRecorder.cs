using AutoMapper;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Cybercore.Persistence;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Repositories;
using Cybercore.Time;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using Polly;
using Polly.CircuitBreaker;
using Contract = Cybercore.Contracts.Contract;
using Share = Cybercore.Blockchain.Share;
using static Cybercore.Util.ActionUtils;

namespace Cybercore.Mining
{
    public class ShareRecorder : BackgroundService
    {
        public ShareRecorder(IConnectionFactory cf,
            IMapper mapper,
            JsonSerializerSettings jsonSerializerSettings,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            ClusterConfig clusterConfig,
            IMessageBus messageBus)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(jsonSerializerSettings, nameof(jsonSerializerSettings));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.cf = cf;
            this.mapper = mapper;
            this.jsonSerializerSettings = jsonSerializerSettings;
            this.messageBus = messageBus;
            this.clusterConfig = clusterConfig;

            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;

            pools = clusterConfig.Pools.ToDictionary(x => x.Id, x => x);

            BuildFaultHandlingPolicy();
            ConfigureRecovery();
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IShareRepository shareRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly IMessageBus messageBus;
        private readonly ClusterConfig clusterConfig;
        private readonly Dictionary<string, PoolConfig> pools;
        private readonly IMapper mapper;

        private IAsyncPolicy faultPolicy;
        private bool hasLoggedPolicyFallbackFailure;
        private string recoveryFilename;
        private const int RetryCount = 3;
        private const string PolicyContextKeyShares = "share";
        private bool notifiedAdminOnPolicyFallback = false;

        private async Task PersistSharesAsync(IList<Share> shares)
        {
            var context = new Dictionary<string, object> { { PolicyContextKeyShares, shares } };

            await faultPolicy.ExecuteAsync(ctx => PersistSharesCoreAsync((IList<Share>)ctx[PolicyContextKeyShares]), context);
        }

        private async Task PersistSharesCoreAsync(IList<Share> shares)
        {
            await cf.RunTx(async (con, tx) =>
            {
                var mapped = shares.Select(mapper.Map<Persistence.Model.Share>).ToArray();
                await shareRepo.BatchInsertAsync(con, tx, mapped);

                foreach (var share in shares)
                {
                    if (!share.IsBlockCandidate)
                        continue;

                    var blockEntity = mapper.Map<Block>(share);
                    blockEntity.Status = BlockStatus.Pending;
                    await blockRepo.InsertAsync(con, tx, blockEntity);

                    if (pools.TryGetValue(share.PoolId, out var poolConfig))
                        messageBus.NotifyBlockFound(share.PoolId, blockEntity, poolConfig.Template);
                    else
                        logger.Warn(() => $"Block found for unknown pool {share.PoolId}");
                }
            });
        }

        private static void OnPolicyRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} in {timeSpan} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        private Task OnPolicyFallbackAsync(Exception ex, Context context)
        {
            logger.Warn(() => $"Fallback due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
            return Task.FromResult(true);
        }

        private async Task OnExecutePolicyFallbackAsync(Context context, CancellationToken ct)
        {
            var shares = (IList<Share>)context[PolicyContextKeyShares];

            try
            {
                await using (var stream = new FileStream(recoveryFilename, FileMode.Append, FileAccess.Write))
                {
                    await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        if (stream.Length == 0)
                            WriteRecoveryFileheader(writer);

                        foreach (var share in shares)
                        {
                            var json = JsonConvert.SerializeObject(share, jsonSerializerSettings);
                            await writer.WriteLineAsync(json);
                        }
                    }
                }

                NotifyAdminOnPolicyFallback();
            }

            catch (Exception ex)
            {
                if (!hasLoggedPolicyFallbackFailure)
                {
                    logger.Fatal(ex, "Fatal error during policy fallback execution. Share(s) will be lost!");
                    hasLoggedPolicyFallbackFailure = true;
                }
            }
        }

        private static void WriteRecoveryFileheader(TextWriter writer)
        {
            writer.WriteLine("# The existence of this file means shares could not be committed to the database.");
            writer.WriteLine("# You should stop the pool cluster and run the following command:");
            writer.WriteLine("# dotnet Cybercore.dll -c <path-to-config> -rs <path-to-this-file>\n");
        }

        public async Task RecoverSharesAsync(ClusterConfig clusterConfig, string recoveryFilename)
        {
            logger.Info(() => $"Recovering shares using {recoveryFilename} ...");

            try
            {
                var successCount = 0;
                var failCount = 0;
                const int bufferSize = 100;

                await using (var stream = new FileStream(recoveryFilename, FileMode.Open, FileAccess.Read))
                {
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        var shares = new List<Share>();
                        var lastProgressUpdate = DateTime.UtcNow;

                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine().Trim();

                            if (line.Length == 0)
                                continue;

                            if (line.StartsWith("#"))
                                continue;

                            try
                            {
                                var share = JsonConvert.DeserializeObject<Share>(line, jsonSerializerSettings);
                                shares.Add(share);
                            }

                            catch (JsonException ex)
                            {
                                logger.Error(ex, () => $"Unable to parse share record: {line}");
                                failCount++;
                            }

                            try
                            {
                                if (shares.Count == bufferSize)
                                {
                                    await PersistSharesCoreAsync(shares);

                                    successCount += shares.Count;
                                    shares.Clear();
                                }
                            }

                            catch (Exception ex)
                            {
                                logger.Error(ex, () => "Unable to import shares");
                                failCount++;
                            }

                            var now = DateTime.UtcNow;
                            if (now - lastProgressUpdate > TimeSpan.FromSeconds(10))
                            {
                                logger.Info($"{successCount} shares imported");
                                lastProgressUpdate = now;
                            }
                        }

                        try
                        {
                            if (shares.Count > 0)
                            {
                                await PersistSharesCoreAsync(shares);

                                successCount += shares.Count;
                            }
                        }

                        catch (Exception ex)
                        {
                            logger.Error(ex, () => "Unable to import shares");
                            failCount++;
                        }
                    }
                }

                if (failCount == 0)
                    logger.Info(() => $"Successfully imported {successCount} shares");
                else
                    logger.Warn(() => $"Successfully imported {successCount} shares with {failCount} failures");
            }

            catch (FileNotFoundException)
            {
                logger.Error(() => $"Recovery file {recoveryFilename} was not found");
            }
        }

        private void NotifyAdminOnPolicyFallback()
        {
            if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true &&
                !notifiedAdminOnPolicyFallback)
            {
                notifiedAdminOnPolicyFallback = true;

                messageBus.SendMessage(new AdminNotification("Share Recorder Policy Fallback",
                    $"The Share Recorder's Policy Fallback has been engaged. Check share recovery file {recoveryFilename}."));
            }
        }

        private void ConfigureRecovery()
        {
            recoveryFilename = !string.IsNullOrEmpty(clusterConfig.ShareRecoveryFile)
                ? clusterConfig.ShareRecoveryFile
                : "recovered-shares.txt";
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    OnPolicyRetry);

            var breaker = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .CircuitBreakerAsync(2, TimeSpan.FromMinutes(1));

            var fallback = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .FallbackAsync(OnExecutePolicyFallbackAsync, OnPolicyFallbackAsync);

            var fallbackOnBrokenCircuit = Policy
                .Handle<BrokenCircuitException>()
                .FallbackAsync(OnExecutePolicyFallbackAsync, (ex, context) => Task.FromResult(true));

            faultPolicy = Policy.WrapAsync(
                fallbackOnBrokenCircuit,
                Policy.WrapAsync(fallback, breaker, retry));
        }

        protected override Task ExecuteAsync(CancellationToken ct)
        {
            logger.Info(() => "Online");

            return messageBus.Listen<StratumShare>()
                .ObserveOn(TaskPoolScheduler.Default)
                .Where(x => x.Share != null)
                .Select(x => x.Share)
                .Buffer(TimeSpan.FromSeconds(5), 250)
                .Where(shares => shares.Any())
                .Select(shares => Observable.FromAsync(() =>
                    Guard(() =>
                        PersistSharesAsync(shares),
                            ex => logger.Error(ex))))
                .Concat()
                .ToTask(ct)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                        logger.Fatal(() => $"Terminated due to error {task.Exception?.InnerException ?? task.Exception}");
                    else
                        logger.Info(() => "Offline");
                }, ct);
        }
    }
}