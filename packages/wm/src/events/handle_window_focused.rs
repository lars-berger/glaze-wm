use anyhow::Context;
use tracing::info;
use wm_common::{DisplayState, WindowRuleEvent};
use wm_platform::NativeWindow;

use crate::{
  commands::{
    container::set_focused_descendant, window::run_window_rules,
    workspace::focus_workspace,
  },
  models::WorkspaceTarget,
  traits::{CommonGetters, WindowGetters},
  user_config::UserConfig,
  wm_state::WmState,
};

pub fn handle_window_focused(
  native_window: &NativeWindow,
  state: &mut WmState,
  config: &mut UserConfig,
) -> anyhow::Result<()> {
  let found_window = state.window_from_native(native_window);
  let focused_container =
    state.focused_container().context("No focused container.")?;

  // Handle overriding focus on close/minimize. After a window is closed
  // or minimized, the OS or the closed application might automatically
  // switch focus to a different window. To force focus to go to the WM's
  // target focus container, we reassign any focus events 100ms after
  // close/minimize. This will cause focus to briefly flicker to the OS
  // focus target and then to the WM's focus target.
  if !focused_container.is_workspace()
    && state.focused_container() != found_window.clone().map(Into::into)
    && state
      .unmanaged_or_minimized_timestamp
      .is_some_and(|time| time.elapsed().as_millis() < 100)
  {
    state.pending_sync.queue_focus_change();
    return Ok(());
  }

  // Ignore the focus event if window is being hidden by the WM.
  if let Some(window) = &found_window {
    if window.display_state() == DisplayState::Hiding {
      return Ok(());
    }
  }

  // Focus effect should be updated for any change in focus that shouldn't
  // be overwritten. Focus here is either:
  //  1. The WM's focused window.
  //  2. A workspace (i.e. the desktop window).
  //  3. An ignored window.
  state.pending_sync.queue_focused_effect_update();

  if let Some(window) = found_window {
    let workspace = window.workspace().context("No workspace")?;

    // Native focus has been synced to the WM's focused container.
    if focused_container == window.clone().into() {
      state.pending_sync.queue_workspace_to_reorder(workspace);
      return Ok(());
    }

    info!("Window manually focused: {window}");

    // Handle focus events from windows on hidden workspaces. For example,
    // if Discord is forcefully shown by the OS when it's on a hidden
    // workspace, switch focus to Discord's workspace.
    if window.display_state() == DisplayState::Hidden {
      info!("Focusing off-screen window: {window}");

      focus_workspace(
        WorkspaceTarget::Name(workspace.config().name),
        state,
        config,
      )?;
    }

    // Update the WM's focus state.
    set_focused_descendant(&window.clone().into(), None);

    // Run window rules for focus events.
    run_window_rules(window, &WindowRuleEvent::Focus, state, config)?;

    state.pending_sync.queue_workspace_to_reorder(workspace);
  }

  Ok(())
}
