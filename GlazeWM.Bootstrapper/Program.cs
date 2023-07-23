using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CommandLine;
using GlazeWM.Bar;
using GlazeWM.Domain;
using GlazeWM.Domain.Containers;
using GlazeWM.Infrastructure;
using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Infrastructure.Exceptions;
using GlazeWM.Infrastructure.Logging;
using GlazeWM.Infrastructure.Serialization;
using GlazeWM.Interprocess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace GlazeWM.Bootstrapper
{
  internal static class Program
  {
    [Verb("command", HelpText = "Invoke a WM command (eg. `command \"focus workspace 1\"`).")]
    public class InvokeCommandOptions
    {
      [Value(
        0,
        MetaName = "meta",
        Required = true,
        HelpText = "Input file-name including path"
      )]
      public string Command { get; set; }

      [Option(
        'c',
        "context-container-id",
        Required = false,
        HelpText = "ID of container to use as context."
      )]
      public string ContextContainerId { get; set; }
    }

    [Verb("commit", HelpText = "Subscribe to a WM event (eg. `subscribe -e window_focus,window_close`)")]
    public class SubscribeOptions
    {
      [Option(
        'e',
        "events",
        Required = true,
        HelpText = "WM events to subscribe to."
      )]
      public bool Events { get; set; }
    }

    [Verb(
      "containers",
      HelpText = "Get all containers (monitors, workspaces, windows, split containers)."
    )]
    public class GetContainersOptions
    {
    }

    [Verb("monitors", HelpText = "Get all monitors.")]
    public class GetMonitorsOptions
    {
    }

    [Verb("workspaces", HelpText = "Get all workspaces.")]
    public class GetWorkspacesOptions
    {
    }

    [Verb("windows", HelpText = "Get all windows.")]
    public class GetWindowsOptions
    {
    }

    private const string APP_GUID = "325d0ed7-7f60-4925-8d1b-aa287b26b218";

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
      Debug.WriteLine("Application started.");
      Console.WriteLine($"Application started. {args}");
      var splitArgs = string.Join(",", args).Split(" ");
      Console.WriteLine($"Application started. {splitArgs}");

      var x = Parser.Default.ParseArguments<
        InvokeCommandOptions,
        SubscribeOptions
      // GetContainersOptions,
      // GetMonitorsOptions,
      // GetWorkspacesOptions,
      // GetWindowsOptions
      >(args);

      // MessageBox.Show($"x.Value.Command {x.Value.Command}");
      // MessageBox.Show($"x.Value.Command {x.Value.Command1}");
      // MessageBox.Show($"x.Value.Command {x.Value.Command2}");
      // MessageBox.Show($"x.Value.Command {x.Value.Command3}");
      // Console.WriteLine($"x.Value.Command {x.Value.Command}");
      // Debug.WriteLine($"x.Value.Command {x.Value.Command}");
      // Environment.Exit(1);

      // var x = Parser.Default.(splitArgs);
      // MapResult(
      //   (InvokeCommandOptions opts) => Debug.WriteLine("jfdiosao"),
      //   (SubscribeOptions opts) => Debug.WriteLine("jfdiosao"),
      //   (GetContainersOptions opts) => Debug.WriteLine("jfdiosao"),
      //   _ => Debug.WriteLine("jfdiosao")
      // );

      // Prevent multiple app instances using a global UUID mutex.
      using var mutex = new Mutex(false, "Global\\" + APP_GUID, out var createdNew);

      // return Parser.Default.ParseArguments<
      //   StartupOptions,
      //   InvokeCommandOptions
      //   SubscribeOptions,
      //   GetContainersOptions,
      //   GetMonitorsOptions,
      //   GetWorkspacesOptions,
      //   GetWindowsOptions
      // >(args).MapResult(
      //   (StartupOptions opts) => WindowManager.Startup(opts, createdNew),
      //   _ => Debug.WriteLine("jfdiosao"),
      // );

      if (!mutex.WaitOne(0, false))
      {
        Debug.Write(
          "Application is already running. Only one instance of this application is allowed."
        );
        return;
      }

      var host = CreateHost(args);
      ServiceLocator.Provider = host.Services;

      var startup = ServiceLocator.GetRequiredService<Startup>();
      startup.Run();
    }

    private static IHost CreateHost(string[] args)
    {
      return Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(appConfig =>
        {
          appConfig.AddCommandLine(args, new Dictionary<string, string>
          {
            // Map CLI argument `--config` to `UserConfigPath` configuration key.
            {"--config", "UserConfigPath"}
          });
        })
        .ConfigureServices((_, services) =>
        {
          services.AddInfrastructureServices();
          services.AddDomainServices();
          services.AddBarServices();
          services.AddInterprocessServices();
          services.AddSingleton<Startup>();

          // Configure exception handler.
          services
            .AddOptions<ExceptionHandlingOptions>()
            .Configure<Bus, ContainerService>((options, bus, containerService) =>
            {
              options.ErrorLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "./.glaze-wm/errors.log"
              );

              options.ErrorLogMessageDelegate = (Exception exception) =>
              {
                var serializeOptions = JsonParser.OptionsFactory(
                  (options) => options.Converters.Add(new JsonContainerConverter())
                );

                var stateDump = JsonParser.ToString(
                  containerService.ContainerTree,
                  serializeOptions
                );

                // History of latest command invocations. Most recent is first.
                var commandHistory = bus.CommandHistory
                  .Select(command => command.Name)
                  .Reverse();

                return $"{DateTime.Now}\n"
                  + $"{exception}\n"
                  + $"Command history: {string.Join(", ", commandHistory)} \n"
                  + $"State dump: {stateDump}\n\n";
              };
            });
        })
        .ConfigureLogging(builder =>
        {
          builder.ClearProviders();
          builder.AddConsole(options => options.FormatterName = "customFormatter")
            .AddConsoleFormatter<LogFormatter, ConsoleFormatterOptions>();
        })
        .Build();
    }
  }
}
