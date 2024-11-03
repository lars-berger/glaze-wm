use crate::{
  common::platform::NativeWindow,
  windows::{traits::WindowGetters, ActiveDrag},
  wm_state::WmState,
};

/// Handles the event for when a window is started being moved or resized
/// by the user (e.g. via the window's drag handles).
pub fn handle_window_moved_or_resized_start(
  native_window: NativeWindow,
  state: &mut WmState,
) -> anyhow::Result<()> {
  if state.paused {
    return Ok(());
  }

  let found_window = state.window_from_native(&native_window);

  if let Some(found_window) = found_window {
    found_window.set_active_drag(Some(ActiveDrag {
      operation: None,
      is_from_tiling: found_window.is_tiling_window(),
    }));
  }

  Ok(())
}
