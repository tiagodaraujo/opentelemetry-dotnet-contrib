// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
#if !NETFRAMEWORK
using System.Net.Sockets;
#endif
using OpenTelemetry.Instrumentation.StackExchangeRedis.Tests;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using Xunit;

namespace OpenTelemetry.Instrumentation.StackExchangeRedis.Implementation;

[Collection("Redis")]
public class RedisProfilerEntryInstrumenterTests : IDisposable
{
    private readonly ConnectionMultiplexer connection;
    private readonly RedisMetrics metrics;
    private readonly TracerProvider tracerProvider;

    public RedisProfilerEntryInstrumenterTests()
    {
        var connectionOptions = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
        };
        connectionOptions.EndPoints.Add("localhost:6379");

        this.connection = ConnectionMultiplexer.Connect(connectionOptions);
        this.metrics = new RedisMetrics();

        this.tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddRedisInstrumentation(this.connection)
            .Build()!;
    }

    public void Dispose()
    {
        this.tracerProvider.Dispose();
        this.connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesCommandAsName()
    {
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.Equal("SET", result.DisplayName);
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesNamespace()
    {
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeDbNamespace));
        Assert.Equal(0, result.GetTagValue(SemanticConventions.AttributeDbNamespace));
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesTimestampAsStartTime()
    {
        var now = DateTimeOffset.Now;
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(now.DateTime);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.Equal(now, result.StartTimeUtc);
    }

    [Fact]
    public void ProfilerCommandInstrument_SetsDbTypeAttributeAsRedis()
    {
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeDbSystem));
        Assert.Equal("redis", result.GetTagValue(SemanticConventions.AttributeDbSystem));
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesCommandAsDbStatementAttribute()
    {
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeDbOperationName));
        Assert.Equal("SET", result.GetTagValue(SemanticConventions.AttributeDbOperationName));
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeDbQueryText));
        Assert.Equal("SET", result.GetTagValue(SemanticConventions.AttributeDbQueryText));
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesFlagsForFlagsAttribute()
    {
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow, CommandFlags.FireAndForget | CommandFlags.NoRedirect);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeDbRedisFlagsKeyName));

#if NET8_0
        Assert.Equal("FireAndForget, NoRedirect", result.GetTagValue(SemanticConventions.AttributeDbRedisFlagsKeyName));
#else
        Assert.Equal("PreferMaster, FireAndForget, NoRedirect", result.GetTagValue(SemanticConventions.AttributeDbRedisFlagsKeyName));
#endif
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesIpEndPointAsEndPoint()
    {
        long address = 1;
        int port = 2;
        string addressString = $"{address}.0.0.0";

        var activity = new Activity("redis-profiler");
        IPEndPoint ipLocalEndPoint = new IPEndPoint(address, port);
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow, ipLocalEndPoint);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeServerAddress));
        Assert.Equal(addressString, result.GetTagValue(SemanticConventions.AttributeServerAddress));
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeServerPort));
        Assert.Equal(port, result.GetTagValue(SemanticConventions.AttributeServerPort));
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeNetworkPeerAddress));
        Assert.Equal(addressString, result.GetTagValue(SemanticConventions.AttributeNetworkPeerAddress));
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeNetworkPeerPort));
        Assert.Equal(port, result.GetTagValue(SemanticConventions.AttributeNetworkPeerPort));
    }

    [Fact]
    public void ProfilerCommandInstrument_UsesDnsEndPointAsEndPoint()
    {
        var dnsEndPoint = new DnsEndPoint("https://opentelemetry.io/", 443);

        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow, dnsEndPoint);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeServerAddress));
        Assert.Equal(dnsEndPoint.Host, result.GetTagValue(SemanticConventions.AttributeServerAddress));
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeServerPort));
        Assert.Equal(dnsEndPoint.Port, result.GetTagValue(SemanticConventions.AttributeServerPort));
    }

#if !NETFRAMEWORK
    [Fact]
    public void ProfilerCommandInstrument_UsesOtherEndPointAsEndPoint()
    {
        var unixEndPoint = new UnixDomainSocketEndPoint("https://opentelemetry.io/");
        var activity = new Activity("redis-profiler");
        var profiledCommand = new TestProfiledCommand(DateTime.UtcNow, unixEndPoint);

        var result = RedisProfilerEntryInstrumenter.ProfilerCommandInstrument(activity, profiledCommand, this.metrics, new StackExchangeRedisInstrumentationOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.GetTagValue(SemanticConventions.AttributeServerAddress));
        Assert.Equal(unixEndPoint.ToString(), result.GetTagValue(SemanticConventions.AttributeServerAddress));
        Assert.Null(result.GetTagValue(SemanticConventions.AttributeServerPort));
    }
#endif
}