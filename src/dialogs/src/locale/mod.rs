mod constants;
pub mod strings;

use constants::{EN_US_FTL, RU_FTL};
use fluent::FluentArgs;
use fluent_bundle::concurrent::FluentBundle;
use fluent_bundle::FluentResource;
use std::sync::OnceLock;

static BUNDLE: OnceLock<FluentBundle<FluentResource>> = OnceLock::new();

/// Detect the system locale and pick the best available .ftl resource.
/// Falls back to en-US if no match.
fn select_locale() -> (&'static str, &'static str) {
    if let Some(locale_str) = sys_locale::get_locale() {
        let lower = locale_str.to_lowercase();
        if lower.starts_with("ru") {
            return ("ru", RU_FTL);
        }
    }
    ("en-US", EN_US_FTL)
}

fn get_bundle() -> &'static FluentBundle<FluentResource> {
    BUNDLE.get_or_init(|| {
        let (lang_tag, ftl_source) = select_locale();
        info!("Locale selected: {}", lang_tag);
        let resource = FluentResource::try_new(ftl_source.to_string()).expect("Failed to parse Fluent resource");
        let lang_id: unic_langid::LanguageIdentifier = lang_tag.parse().expect("Failed to parse language identifier");
        let mut bundle = FluentBundle::new_concurrent(vec![lang_id]);
        bundle.add_resource(resource).expect("Failed to add Fluent resource");
        bundle
    })
}

pub fn init_localization() {
    let _ = get_bundle();
}

pub(crate) fn format_message(id: &str, args: Option<&FluentArgs>) -> String {
    let bundle = get_bundle();
    let msg = match bundle.get_message(id) {
        Some(m) => m,
        None => {
            warn!("Missing fluent message: {}", id);
            return id.to_string();
        }
    };
    let pattern = match msg.value() {
        Some(p) => p,
        None => {
            warn!("Fluent message has no value: {}", id);
            return id.to_string();
        }
    };
    let mut errors = Vec::new();
    let result = bundle.format_pattern(pattern, args, &mut errors);
    if !errors.is_empty() {
        warn!("Fluent formatting errors for '{}': {:?}", id, errors);
    }
    result.to_string()
}
