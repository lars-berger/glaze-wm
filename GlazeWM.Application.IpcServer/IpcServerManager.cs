﻿using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using CommandLine;
using GlazeWM.Application.IpcServer.Messages;
using GlazeWM.Application.IpcServer.Server;
using GlazeWM.Domain.Containers;
using GlazeWM.Domain.UserConfigs;
using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace GlazeWM.Application.IpcServer
{
  public sealed class IpcServerManager : IDisposable
  {
    private readonly Bus _bus;
    private readonly ILogger<IpcServerManager> _logger;
    private readonly UserConfigService _userConfigService;

    /// <summary>
    /// The websocket server instance.
    /// </summary>
    private Server.IpcServer _server { get; set; }

    private readonly Subject<bool> _serverKill = new();

    private readonly JsonSerializerOptions _serializeOptions =
      JsonParser.OptionsFactory((options) =>
        options.Converters.Add(new JsonContainerConverter())
      );

    public IpcServerManager(
      Bus bus,
      ILogger<IpcServerManager> logger,
      UserConfigService userConfigService)
    {
      _bus = bus;
      _logger = logger;
      _userConfigService = userConfigService;
    }

    /// <summary>
    /// Start the IPC server on user-specified port.
    /// </summary>
    public void StartServer()
    {
      var port = _userConfigService.GeneralConfig.IpcServerPort;
      _server = new(port);

      // Start listening for messages.
      _server.Start();
      _server.Messages
        .TakeUntil(_serverKill)
        .Subscribe(message => HandleMessage(message));

      // Broadcast events to all sessions.
      _bus.Events
        .TakeUntil(_serverKill)
        .Subscribe(@event => BroadcastEvent(@event));

      _logger.LogDebug("Started IPC server on port {Port}.", port);
    }

    /// <summary>
    /// Kill the IPC server.
    /// </summary>
    public void StopServer()
    {
      if (_server is null)
        return;

      _serverKill.OnNext(true);
      _server.Stop();
      _logger.LogDebug("Stopped IPC server on port {Port}.", _server.Port);
    }

    private void HandleMessage(IncomingIpcMessage message)
    {
      _logger.LogDebug(
        "IPC message from session {Session}: {Text}.",
        message.SessionId,
        message.Text
      );

      var ipcMessage = message.Text.Split(" ");

      Parser.Default.ParseArguments<
        InvokeCommandMessage,
        SubscribeMessage,
        GetContainersMessage,
        GetMonitorsMessage,
        GetWorkspacesMessage,
        GetWindowsMessage
      >(ipcMessage).MapResult(
        (InvokeCommandMessage message) => 1,
        (SubscribeMessage message) => 1,
        (GetContainersMessage message) => 1,
        (GetMonitorsMessage message) => 1,
        (GetWorkspacesMessage message) => 1,
        (GetWindowsMessage message) => 1,
        _ => 1
      );
    }

    private void BroadcastEvent(Event @event)
    {
      var eventJson = JsonParser.ToString((dynamic)@event, _serializeOptions);
      _server.MulticastText(eventJson);
    }

    public void Dispose()
    {
      if (_server?.IsDisposed != true)
        _server.Dispose();
    }
  }
}
