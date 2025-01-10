use std::{iter, path::PathBuf};

use clap::{error::KindFormatter, Args, Parser, ValueEnum};
use serde::{Deserialize, Deserializer, Serialize};
use uuid::Uuid;

use crate::{Direction, LengthValue, OpacityValue, TilingDirection};

const VERSION: &str = env!("VERSION_NUMBER");

#[derive(Clone, Debug, Parser)]
#[clap(author, version = VERSION, about, long_about = None)]
pub enum AppCommand {
  /// Starts the window manager.
  Start {
    /// Custom path to user config file.
    ///
    /// The default path is `%userprofile%/.glzr/glazewm/config.yaml`
    #[clap(short = 'c', long = "config", value_hint = clap::ValueHint::FilePath)]
    config_path: Option<PathBuf>,

    #[clap(flatten)]
    verbosity: Verbosity,
  },

  /// Retrieves and outputs a specific part of the window manager's state.
  ///
  /// Requires an already running instance of the window manager.
  #[clap(alias = "q")]
  Query {
    #[clap(subcommand)]
    command: QueryCommand,
  },

  /// Invokes a window manager command.
  ///
  /// Requires an already running instance of the window manager.
  #[clap(alias = "c")]
  Command {
    #[clap(long = "id")]
    subject_container_id: Option<Uuid>,

    #[clap(subcommand)]
    command: InvokeCommand,
  },

  /// Subscribes to one or more WM events (e.g. `window_close`), and
  /// continuously outputs the incoming events.
  ///
  /// Requires an already running instance of the window manager.
  Sub {
    /// WM event(s) to subscribe to.
    #[clap(short = 'e', long, value_enum, num_args = 1..)]
    events: Vec<SubscribableEvent>,
  },

  /// Unsubscribes from a prior event subscription.
  ///
  /// Requires an already running instance of the window manager.
  Unsub {
    /// Subscription ID to unsubscribe from.
    #[clap(long = "id")]
    subscription_id: Uuid,
  },
}

impl AppCommand {
  /// Parses `AppCommand` from command line arguments.
  ///
  /// Defaults to `AppCommand::Start` if no arguments are provided.
  pub fn parse_with_default(args: &Vec<String>) -> Self {
    match args.len() == 1 {
      true => AppCommand::Start {
        config_path: None,
        verbosity: Verbosity {
          verbose: false,
          quiet: false,
        },
      },
      false => AppCommand::parse_from(args),
    }
  }
}

/// Verbosity flags to be used with `#[command(flatten)]`.
#[derive(Args, Clone, Debug)]
#[clap(about = None, long_about = None)]
pub struct Verbosity {
  /// Enables verbose logging.
  #[clap(short = 'v', long, action)]
  verbose: bool,

  /// Disables logging.
  #[clap(short = 'q', long, action, conflicts_with = "verbose")]
  quiet: bool,
}

#[derive(Clone, Debug, Parser)]
pub enum QueryCommand {
  /// Outputs metadata about the application (e.g. version number).
  AppMetadata,
  /// Outputs the active binding modes.
  BindingModes,
  /// Outputs the focused container (either a window or an empty
  /// workspace).
  Focused,
  /// Outputs the tiling direction of the focused container.
  TilingDirection,
  /// Outputs all monitors.
  Monitors,
  /// Outputs all windows.
  Windows,
  /// Outputs all active workspaces.
  Workspaces,
  /// Outputs whether the window manager is paused.
  Paused,
}

#[derive(Clone, Debug, PartialEq, ValueEnum)]
#[clap(rename_all = "snake_case")]
pub enum SubscribableEvent {
  All,
  ApplicationExiting,
  BindingModesChanged,
  FocusChanged,
  FocusedContainerMoved,
  MonitorAdded,
  MonitorUpdated,
  MonitorRemoved,
  TilingDirectionChanged,
  UserConfigChanged,
  WindowManaged,
  WindowUnmanaged,
  WorkspaceActivated,
  WorkspaceDeactivated,
  WorkspaceUpdated,
  PauseChanged,
}

#[derive(Clone, Debug, Parser, PartialEq, Serialize)]
pub enum InvokeCommand {
  AdjustBorders(InvokeAdjustBordersCommand),
  Close,
  Focus(InvokeFocusCommand),
  Ignore,
  Move(InvokeMoveCommand),
  MoveWorkspace {
    #[clap(long)]
    direction: Direction,
  },
  Position(InvokePositionCommand),
  Resize(InvokeResizeCommand),
  SetFloating {
    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    shown_on_top: Option<bool>,

    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    centered: Option<bool>,

    #[clap(long, allow_hyphen_values = true)]
    x_pos: Option<i32>,

    #[clap(long, allow_hyphen_values = true)]
    y_pos: Option<i32>,

    #[clap(long, allow_hyphen_values = true)]
    width: Option<LengthValue>,

    #[clap(long, allow_hyphen_values = true)]
    height: Option<LengthValue>,
  },
  SetFullscreen {
    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    shown_on_top: Option<bool>,

    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    maximized: Option<bool>,
  },
  SetMinimized,
  SetTiling,
  SetTitleBarVisibility {
    #[clap(required = true, value_enum)]
    visibility: TitleBarVisibility,
  },
  SetOpacity {
    #[clap(required = true, allow_hyphen_values = true)]
    opacity: OpacityValue,
  },
  ShellExec {
    #[clap(long, action)]
    hide_window: bool,

    #[clap(required = true, trailing_var_arg = true)]
    command: Vec<String>,
  },
  // Reuse `InvokeResizeCommand` struct.
  Size(InvokeResizeCommand),
  ToggleFloating {
    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    shown_on_top: Option<bool>,

    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    centered: Option<bool>,
  },
  ToggleFullscreen {
    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    shown_on_top: Option<bool>,

    #[clap(long, default_missing_value = "true", require_equals = true, num_args = 0..=1)]
    maximized: Option<bool>,
  },
  ToggleMinimized,
  ToggleTiling,
  ToggleTilingDirection,
  SetTilingDirection {
    #[clap(required = true)]
    tiling_direction: TilingDirection,
  },
  WmCycleFocus {
    #[clap(long, default_value_t = false)]
    omit_fullscreen: bool,

    #[clap(long, default_value_t = true)]
    omit_minimized: bool,
  },
  WmDisableBindingMode {
    #[clap(long)]
    name: String,
  },
  WmEnableBindingMode {
    #[clap(long)]
    name: String,
  },
  WmExit,
  WmRedraw,
  WmReloadConfig,
  WmTogglePause,
}

impl<'de> Deserialize<'de> for InvokeCommand {
  fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
  where
    D: Deserializer<'de>,
  {
    // Clap expects an array of string slices where the first argument is
    // the binary name/path. When deserializing commands from the user
    // config, we therefore have to prepend an additional empty argument.
    let unparsed = String::deserialize(deserializer)?;
    let unparsed_split = iter::once("").chain(unparsed.split_whitespace());

    InvokeCommand::try_parse_from(unparsed_split).map_err(|err| {
      // Format the error message and remove the "error: " prefix.
      let err_msg = err.apply::<KindFormatter>().to_string();
      serde::de::Error::custom(err_msg.trim_start_matches("error: "))
    })
  }
}

#[derive(Clone, Debug, PartialEq, Serialize, ValueEnum)]
#[clap(rename_all = "snake_case")]
#[serde(rename_all = "snake_case")]
pub enum TitleBarVisibility {
  Shown,
  Hidden,
}

#[derive(Args, Clone, Debug, PartialEq, Serialize)]
#[group(required = true, multiple = true)]
pub struct InvokeAdjustBordersCommand {
  #[clap(long, allow_hyphen_values = true)]
  top: Option<LengthValue>,

  #[clap(long, allow_hyphen_values = true)]
  right: Option<LengthValue>,

  #[clap(long, allow_hyphen_values = true)]
  bottom: Option<LengthValue>,

  #[clap(long, allow_hyphen_values = true)]
  left: Option<LengthValue>,
}

#[derive(Args, Clone, Debug, PartialEq, Serialize)]
#[group(required = true, multiple = false)]
pub struct InvokeFocusCommand {
  #[clap(long)]
  direction: Option<Direction>,

  #[clap(long)]
  workspace: Option<String>,

  #[clap(long)]
  monitor: Option<usize>,

  #[clap(long)]
  next_active_workspace: bool,

  #[clap(long)]
  prev_active_workspace: bool,

  #[clap(long)]
  next_workspace: bool,

  #[clap(long)]
  prev_workspace: bool,

  #[clap(long)]
  recent_workspace: bool,
}

#[derive(Args, Clone, Debug, PartialEq, Serialize)]
#[group(required = true, multiple = false)]
pub struct InvokeMoveCommand {
  /// Direction to move the window.
  #[clap(long)]
  direction: Option<Direction>,

  /// Name of workspace to move the window.
  #[clap(long)]
  workspace: Option<String>,

  #[clap(long)]
  next_active_workspace: bool,

  #[clap(long)]
  prev_active_workspace: bool,

  #[clap(long)]
  next_workspace: bool,

  #[clap(long)]
  prev_workspace: bool,

  #[clap(long)]
  recent_workspace: bool,
}

#[derive(Args, Clone, Debug, PartialEq, Serialize)]
#[group(required = true, multiple = true)]
pub struct InvokeResizeCommand {
  #[clap(long, allow_hyphen_values = true)]
  width: Option<LengthValue>,

  #[clap(long, allow_hyphen_values = true)]
  height: Option<LengthValue>,
}

#[derive(Args, Clone, Debug, PartialEq, Serialize)]
#[group(required = true, multiple = true)]
pub struct InvokePositionCommand {
  #[clap(long, action)]
  centered: bool,

  #[clap(long, allow_hyphen_values = true)]
  x_pos: Option<i32>,

  #[clap(long, allow_hyphen_values = true)]
  y_pos: Option<i32>,
}