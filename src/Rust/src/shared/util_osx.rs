use anyhow::{anyhow, bail, Result};

pub fn wait_for_parent_to_exit() -> Result<()> {
    let id = std::os::unix::process::parent_id();
    if id >= 1 {
        waitpid(id);
    }
}
