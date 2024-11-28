pub fn clap_run_main(program_name: &str, main_inner: fn() -> anyhow::Result<()>) -> anyhow::Result<()> {
    if let Err(e) = main_inner() {
        let error_string = format!("An error has occurred: {:?}", e);
        match e.downcast::<clap::Error>() {
            Ok(downcast) => {
                let output_string = downcast.to_string();
                match downcast.kind() {
                    clap::error::ErrorKind::DisplayHelp => {
                        println!("{output_string}");
                        return Ok(());
                    }
                    clap::error::ErrorKind::DisplayHelpOnMissingArgumentOrSubcommand => {
                        println!("{output_string}");
                        return Ok(());
                    }
                    clap::error::ErrorKind::DisplayVersion => {
                        println!("{output_string}");
                        return Ok(());
                    }
                    _ => {
                        error!("{}", error_string);
                        crate::dialogs::show_error(format!("{program_name} Error").as_str(), None, &error_string);
                        return Err(anyhow::Error::from(downcast));
                    }
                }
            }
            Err(e) => {
                error!("{}", error_string);
                crate::dialogs::show_error(format!("{program_name} Error").as_str(), None, &error_string);
                return Err(e);
            }
        }
    }
    Ok(())
}
