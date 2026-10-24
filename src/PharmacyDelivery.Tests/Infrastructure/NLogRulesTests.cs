using PharmacyDelivery.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Extensions.Logging;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace PharmacyDelivery.Tests.Infrastructure;

/// <summary>The real NLog rules from appsettings*.json: framework logs go to the console only, application logs also to the files (one per level).</summary>
[TestFixture]
public sealed class NLogRulesTests : TestBase
{
    [TestCase("Test")]
    [TestCase("Development")]
    [TestCase("Production")]
    [Property("TestCase", "TC-7.20.4")]
    public void FrameworkLogs_NeverReachTheFile_ApplicationLogsDo(string environment)
    {
        var api = Path.Combine(RepositoryPaths.Root, "src", "PharmacyDelivery.Api");
        var section = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"))
            .AddJsonFile(Path.Combine(api, $"appsettings.{environment}.json"), optional: true)
            .Build()
            .GetSection("NLog");
        using var factory = new LogFactory();
        factory.Setup().SetupExtensions(NLogExtensions.Register);
        var configuration = new NLogLoggingConfiguration(section, factory);

        // A folder per day, a file per level: both are part of the file name of the (async-wrapped) file target.
        var target = configuration.FindTargetByName("file");
        var fileName = ((target as WrapperTargetBase)?.WrappedTarget ?? target) is FileTarget fileTarget
            ? fileTarget.FileName.ToString()
            : null;
        Assert.That(fileName, Does.Contain("/${shortdate}/").And.Contain("${level"), "logs/<date>/<level>.log");

        // Swap the real targets (by name; "async": true wraps them) for in-memory ones.
        var file = new MemoryTarget("file-memory") { Layout = "${logger}|${level}" };
        var console = new MemoryTarget("console-memory") { Layout = "${logger}|${level}" };
        foreach (var rule in configuration.LoggingRules)
        {
            var names = rule.Targets.Select(t => t.Name).ToList();
            rule.Targets.Clear();
            if (names.Contains("file"))
                rule.Targets.Add(file);
            if (names.Any(n => n is "console" or "textConsole"))
                rule.Targets.Add(console);
        }

        factory.Configuration = configuration;
        string[] framework =
        [
            "Microsoft.Hosting.Lifetime", "Microsoft.AspNetCore.Hosting.Diagnostics", "Microsoft.EntityFrameworkCore.Update",
            "System.Net.Http.HttpClient.Default.LogicalHandler"
        ];
        foreach (var name in framework.Append("PharmacyDelivery.Startup").Append("PharmacyDelivery.Services.Concrete.CheckoutService"))
        {
            var logger = factory.GetLogger(name);
            logger.Info("info");
            logger.Warn("warn");
            logger.Error("error");
        }

        factory.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(
                file.Logs.Where(l =>
                    l.StartsWith("Microsoft.", StringComparison.Ordinal) || l.StartsWith("System.", StringComparison.Ordinal)),
                Is.Empty, "framework logs in the file");
            Assert.That(file.Logs,
                Does.Contain("PharmacyDelivery.Startup|Info").And.Contain("PharmacyDelivery.Services.Concrete.CheckoutService|Error"));
            Assert.That(console.Logs, Does.Contain("Microsoft.Hosting.Lifetime|Info"), "host lifetime on the console");
            Assert.That(console.Logs,
                Does.Contain("Microsoft.AspNetCore.Hosting.Diagnostics|Warn").And.Not
                    .Contain("Microsoft.AspNetCore.Hosting.Diagnostics|Info"));
            Assert.That(console.Logs, Does.Contain("System.Net.Http.HttpClient.Default.LogicalHandler|Error"));
        });
    }
}
