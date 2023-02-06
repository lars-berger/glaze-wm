using System;
using System.Linq;
using GlazeWM.Domain.Common.Enums;
using GlazeWM.Domain.Containers;
using GlazeWM.Domain.Containers.Commands;
using GlazeWM.Domain.Containers.Events;
using GlazeWM.Domain.Monitors;
using GlazeWM.Domain.UserConfigs;
using GlazeWM.Domain.Windows.Commands;
using GlazeWM.Domain.Workspaces;
using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Infrastructure.Common.Events;
using GlazeWM.Infrastructure.Utils;
using GlazeWM.Infrastructure.WindowsApi;

namespace GlazeWM.Domain.Windows.CommandHandlers
{
  internal sealed class MoveWindowHandler : ICommandHandler<MoveWindowCommand>
  {
    private readonly Bus _bus;
    private readonly ContainerService _containerService;
    private readonly MonitorService _monitorService;
    private readonly UserConfigService _userConfigService;

    public MoveWindowHandler(
      Bus bus,
      ContainerService containerService,
      MonitorService monitorService,
      UserConfigService userConfigService)
    {
      _bus = bus;
      _containerService = containerService;
      _monitorService = monitorService;
      _userConfigService = userConfigService;
    }

    public CommandResponse Handle(MoveWindowCommand command)
    {
      var windowToMove = command.WindowToMove;
      var direction = command.Direction;

      if (windowToMove is FloatingWindow)
      {
        MoveFloatingWindow(windowToMove as FloatingWindow, direction);
        return CommandResponse.Ok;
      }

      if (windowToMove is TilingWindow)
      {
        MoveTilingWindow(windowToMove as TilingWindow, direction);
        return CommandResponse.Ok;
      }

      return CommandResponse.Fail;
    }

    /// <summary>
    /// Whether the window has a tiling sibling in the given direction.
    /// </summary>
    private static bool HasSiblingInDirection(Window windowToMove, Direction direction)
    {
      if (direction is Direction.Up or Direction.Left)
        return windowToMove != windowToMove.SelfAndSiblingsOfType<IResizable>().First();

      return windowToMove != windowToMove.SelfAndSiblingsOfType<IResizable>().Last();
    }

    private void SwapSiblingContainers(Window windowToMove, Direction direction)
    {
      var siblingInDirection = direction is Direction.Up or Direction.Left
        ? windowToMove.PreviousSiblingOfType<IResizable>()
        : windowToMove.NextSiblingOfType<IResizable>();

      // Swap the window with sibling in given direction.
      if (siblingInDirection is Window)
      {
        var targetIndex = direction is Direction.Up or Direction.Left ?
          siblingInDirection.Index : siblingInDirection.Index + 1;

        _bus.Invoke(
          new MoveContainerWithinTreeCommand(
            windowToMove,
            windowToMove.Parent,
            targetIndex,
            false
          )
        );

        _bus.Invoke(new RedrawContainersCommand());
        return;
      }

      // Move the window into the sibling split container.
      var targetDescendant = _containerService.GetDescendantInDirection(
        siblingInDirection,
        direction.Inverse()
      );
      var targetParent = targetDescendant.Parent as SplitContainer;

      var layoutForDirection = direction.GetCorrespondingLayout();
      var shouldInsertAfter =
        targetParent.Layout != layoutForDirection ||
        direction == Direction.Up ||
        direction == Direction.Left;
      var insertionIndex = shouldInsertAfter ? targetDescendant.Index + 1 : targetDescendant.Index;

      _bus.Invoke(
        new MoveContainerWithinTreeCommand(windowToMove, targetParent, insertionIndex, true)
      );

      _bus.Invoke(new RedrawContainersCommand());
    }

    private void MoveToWorkspaceInDirection(Window windowToMove, Direction direction)
    {
      var monitor = MonitorService.GetMonitorFromChildContainer(windowToMove);
      var monitorInDirection = _monitorService.GetMonitorInDirection(direction, monitor);
      var workspaceInDirection = monitorInDirection?.DisplayedWorkspace;

      if (workspaceInDirection == null)
        return;

      // Since window is crossing monitors, adjustments might need to be made because of DPI.
      if (MonitorService.HasDpiDifference(windowToMove, workspaceInDirection))
        windowToMove.HasPendingDpiAdjustment = true;

      // Update floating placement since the window has to cross monitors.
      windowToMove.FloatingPlacement =
        windowToMove.FloatingPlacement.TranslateToCenter(workspaceInDirection.ToRect());

      // TODO: Descend into container if possible.
      if (direction is Direction.Up or Direction.Left)
        _bus.Invoke(new MoveContainerWithinTreeCommand(windowToMove, workspaceInDirection, true));
      else
        _bus.Invoke(new MoveContainerWithinTreeCommand(windowToMove, workspaceInDirection, 0, true));

      _bus.Invoke(new RedrawContainersCommand());

      // Refresh state in bar of which workspace has focus.
      _bus.Emit(new FocusChangedEvent(windowToMove));
    }

    private void ChangeWorkspaceLayout(Window windowToMove, Direction direction)
    {
      var workspace = WorkspaceService.GetWorkspaceFromChildContainer(windowToMove);

      var layoutForDirection = direction.GetCorrespondingLayout();
      _bus.Invoke(new ChangeContainerLayoutCommand(workspace, layoutForDirection));

      // TODO: Should probably descend into sibling if possible.
      if (HasSiblingInDirection(windowToMove, direction))
        SwapSiblingContainers(windowToMove, direction);

      _bus.Invoke(new RedrawContainersCommand());
    }

    private void InsertIntoAncestor(
      TilingWindow windowToMove,
      Direction direction,
      Container ancestorWithLayout)
    {
      // Traverse up from `windowToMove` to find container where the parent is `ancestorWithLayout`.
      // Then, depending on the direction, insert before or after that container.
      var insertionReference = windowToMove.Ancestors
        .FirstOrDefault(container => container.Parent == ancestorWithLayout);

      var insertionReferenceSibling = direction is Direction.Up or Direction.Left
        ? insertionReference.PreviousSiblingOfType<IResizable>()
        : insertionReference.NextSiblingOfType<IResizable>();

      if (insertionReferenceSibling is SplitContainer)
      {
        // Move the window into the adjacent split container.
        var targetDescendant = _containerService.GetDescendantInDirection(
          insertionReferenceSibling,
          direction.Inverse()
        );
        var targetParent = targetDescendant.Parent as SplitContainer;

        var layoutForDirection = direction.GetCorrespondingLayout();
        var shouldInsertAfter =
          targetParent.Layout != layoutForDirection ||
          direction == Direction.Up ||
          direction == Direction.Left;

        var insertionIndex = shouldInsertAfter
          ? targetDescendant.Index + 1
          : targetDescendant.Index;

        _bus.Invoke(new MoveContainerWithinTreeCommand(windowToMove, targetParent, insertionIndex, true));
      }
      else
      {
        // Move the window into the container above.
        var insertionIndex = (direction is Direction.Up or Direction.Left) ?
          insertionReference.Index : insertionReference.Index + 1;

        _bus.Invoke(new MoveContainerWithinTreeCommand(windowToMove, ancestorWithLayout, insertionIndex, true));
      }

      _bus.Invoke(new RedrawContainersCommand());
    }
    //maybe implement this to WindowRect.cs ?
    private Point GetCenterPoint(Rect rect)
    {
      return new Point
      {
        X = rect.X + (rect.Width / 2),
        Y = rect.Y + (rect.Height / 2),
      };
    }
    private void MoveTilingWindow(TilingWindow windowToMove, Direction direction)
    {
      var layoutForDirection = direction.GetCorrespondingLayout();
      var parentMatchesLayout =
        (windowToMove.Parent as SplitContainer).Layout == direction.GetCorrespondingLayout();

      if (parentMatchesLayout && HasSiblingInDirection(windowToMove, direction))
      {
        SwapSiblingContainers(windowToMove, direction);
        return;
      }

      // Attempt to the move window to workspace in given direction.
      if (parentMatchesLayout && windowToMove.Parent is Workspace)
      {
        MoveToWorkspaceInDirection(windowToMove, direction);
        return;
      }

      // The window cannot be moved within the parent container, so traverse upwards to find a
      // suitable ancestor to move to.
      var ancestorWithLayout = windowToMove.Parent.Ancestors.FirstOrDefault(
        container => (container as SplitContainer)?.Layout == layoutForDirection
      ) as SplitContainer;

      // Change the layout of the workspace to layout for direction.
      if (ancestorWithLayout == null)
      {
        ChangeWorkspaceLayout(windowToMove, direction);
        return;
      }

      InsertIntoAncestor(windowToMove, direction, ancestorWithLayout);
    }

    private void MoveFloatingWindow(Window windowToMove, Direction direction)
    {
      var valueFromConfig = _userConfigService.GeneralConfig.FloatingWindowMoveAmount;

      var amount = UnitsHelper.TrimUnits(valueFromConfig);
      var units = UnitsHelper.GetUnits(valueFromConfig);
      var currentMonitor = MonitorService.GetMonitorFromChildContainer(windowToMove);

      amount = units switch
      {
        //is casting with (int) ok?
        "%" => (int)(amount * currentMonitor.Width / 100),
        "ppt" => (int)(amount * currentMonitor.Width / 100),
        "px" => amount,
        // in case user only provided a number in the config;
        // TODO: somehow validate floating_window_move_amount in config on startup
        _ => amount
        // _ => throw new ArgumentException(null, nameof(amount)),
      };

      var x = windowToMove.FloatingPlacement.X;
      var y = windowToMove.FloatingPlacement.Y;

      _ = direction switch
      {
        Direction.Left => x -= amount,
        Direction.Right => x += amount,
        Direction.Up => y -= amount,
        Direction.Down => y += amount,
        _ => throw new ArgumentException(null, nameof(direction))
      };

      var newPlacement = Rect.FromXYCoordinates(x, y, windowToMove.FloatingPlacement.Width, windowToMove.FloatingPlacement.Height);
      var center = GetCenterPoint(newPlacement);

      bool windowMovedMonitors = false;

      // If new placement wants to cross monitors
      if (center.X > currentMonitor.Width + currentMonitor.X || center.X < currentMonitor.X ||
      center.Y < currentMonitor.Y || center.Y > currentMonitor.Height + currentMonitor.Y)
      {
        var monitorInDirection = _monitorService.GetMonitorInDirection(direction, currentMonitor);
        var workspaceInDirection = monitorInDirection?.DisplayedWorkspace;

        if (workspaceInDirection == null)
        {
          // TODO: snap window center to monitor edge
          return;
        }

        windowMovedMonitors = true;
      }

      windowToMove.FloatingPlacement = newPlacement;

      _containerService.ContainersToRedraw.Add(windowToMove);
      _bus.Invoke(new RedrawContainersCommand());

      if (windowMovedMonitors)
        _bus.Emit(new WindowMovedOrResizedEvent(windowToMove.Handle));
    }
  }
}
