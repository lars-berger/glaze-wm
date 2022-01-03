using System.Linq;
using System.Windows.Forms;
using GlazeWM.Domain.Containers;
using GlazeWM.Domain.Containers.Commands;
using GlazeWM.Domain.Monitors.Commands;
using GlazeWM.Domain.Windows;
using GlazeWM.Domain.Workspaces;
using GlazeWM.Infrastructure.Bussing;

namespace GlazeWM.Domain.Monitors.CommandHandlers
{
  class RefreshMonitorStateHandler : ICommandHandler<RefreshMonitorStateCommand>
  {
    private Bus _bus;
    private MonitorService _monitorService;
    private ContainerService _containerService;
    private WindowService _windowService;
    private WorkspaceService _workspaceService;

    public RefreshMonitorStateHandler(
      Bus bus,
      MonitorService monitorService,
      ContainerService containerService,
      WindowService windowService,
      WorkspaceService workspaceService
    )
    {
      _bus = bus;
      _monitorService = monitorService;
      _containerService = containerService;
      _windowService = windowService;
      _workspaceService = workspaceService;
    }

    public CommandResponse Handle(RefreshMonitorStateCommand command)
    {
      foreach (var screen in Screen.AllScreens)
      {
        var foundMonitor = _monitorService.GetMonitors().FirstOrDefault(
          monitor => monitor.DeviceName == screen.DeviceName
        );

        // Add monitor if it doesn't exist in state.
        if (foundMonitor == null)
        {
          _bus.Invoke(new AddMonitorCommand(screen));
          continue;
        }

        // Update monitor with changes to dimensions and positioning.
        foundMonitor.Width = screen.WorkingArea.Width;
        foundMonitor.Height = screen.WorkingArea.Height;
        foundMonitor.X = screen.WorkingArea.X;
        foundMonitor.Y = screen.WorkingArea.Y;
        foundMonitor.IsPrimary = screen.Primary;
      }

      // Verify that all monitors in state exist in `Screen.AllScreens`.
      var monitorsToRemove = _monitorService.GetMonitors().Where(
        monitor => !IsMonitorActive(monitor)
      );

      // Remove any monitors that no longer exist and move their workspaces to other monitors.
      // TODO: Verify that this works with "Duplicate these displays" or "Show only X" settings.
      foreach (var monitor in monitorsToRemove.ToList())
        _bus.Invoke(new RemoveMonitorCommand(monitor));

      foreach (var window in _windowService.GetWindows())
      {
        // Display setting changes can spread windows out sporadically, so mark all windows as
        // needing a DPI adjustment (just in case).
        window.HasPendingDpiAdjustment = true;

        // Need to update floating position of moved windows when a monitor is disconnected or if
        // the primary display is changed. The primary display dictates the position of 0,0.
        var parentWorkspace = _workspaceService.GetWorkspaceFromChildContainer(window);
        window.FloatingPlacement =
          window.FloatingPlacement.TranslateToCenter(parentWorkspace.ToRectangle());
      }

      // Redraw full container tree.
      _containerService.ContainersToRedraw.Add(_containerService.ContainerTree);
      _bus.Invoke(new RedrawContainersCommand());

      return CommandResponse.Ok;
    }

    private bool IsMonitorActive(Monitor monitor)
    {
      return Screen.AllScreens.Any(screen => screen.DeviceName == monitor.DeviceName);
    }
  }
}