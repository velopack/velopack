use crate::locale_constants::{EN_US_FTL, LOCALE_SOURCES};
use fluent::FluentArgs;
use fluent_bundle::concurrent::FluentBundle;
use fluent_bundle::FluentResource;
use fluent_langneg::{convert_vec_str_to_langids_lossy, negotiate_languages, NegotiationStrategy};
use std::sync::OnceLock;
use unic_langid::LanguageIdentifier as UnicLanguageIdentifier;

static BUNDLE: OnceLock<FluentBundle<FluentResource>> = OnceLock::new();

const DEFAULT_LOCALE: (&str, &str) = ("en-US", EN_US_FTL);

fn negotiate_locale<'a>(requested: &str, available: &'a [(&'a str, &'a str)], default: (&'a str, &'a str)) -> (&'a str, &'a str) {
    let requested = convert_vec_str_to_langids_lossy(&[requested]);
    if requested.is_empty() {
        return default;
    }

    let tags: Vec<&str> = available.iter().map(|(tag, _)| *tag).collect();
    let langids = convert_vec_str_to_langids_lossy(tags.iter());

    let matches = negotiate_languages(&requested, &langids, None, NegotiationStrategy::Matching);

    if let Some(matched) = matches.first() {
        if let Some(idx) = langids.iter().position(|l| l == *matched) {
            return available[idx];
        }
    }

    default
}

/// Detect the system locale and pick the best available .ftl resource.
/// Falls back to en-US if no match.
fn select_locale() -> (&'static str, &'static str) {
    match sys_locale::get_locale() {
        Some(locale) => negotiate_locale(&locale, LOCALE_SOURCES, DEFAULT_LOCALE),
        None => DEFAULT_LOCALE,
    }
}

fn get_bundle() -> &'static FluentBundle<FluentResource> {
    BUNDLE.get_or_init(|| {
        let (lang_tag, ftl_source) = select_locale();
        info!("Locale selected: {}", lang_tag);
        let resource = FluentResource::try_new(ftl_source.to_string()).expect("Failed to parse Fluent resource");
        let lang_id: UnicLanguageIdentifier = lang_tag.parse().expect("Failed to parse language identifier");
        let mut bundle = FluentBundle::new_concurrent(vec![lang_id]);
        // Disable Unicode bidi isolation characters (U+2068/U+2069) around placeables.
        // Native OS dialog APIs (e.g. TaskDialog) render these as visible characters.
        bundle.set_use_isolating(false);
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

#[cfg(test)]
mod tests {
    use super::*;

    const TEST_LOCALES: &[(&str, &str)] = &[("en-US", "english"), ("ru", "russian")];
    const TEST_DEFAULT: (&str, &str) = ("en-US", "english");

    #[test]
    fn exact_match_en_us() {
        assert_eq!(negotiate_locale("en-US", TEST_LOCALES, TEST_DEFAULT), ("en-US", "english"));
    }

    #[test]
    fn exact_match_ru() {
        assert_eq!(negotiate_locale("ru", TEST_LOCALES, TEST_DEFAULT), ("ru", "russian"));
    }

    #[test]
    fn language_only_match_en_gb_to_en_us() {
        assert_eq!(negotiate_locale("en-GB", TEST_LOCALES, TEST_DEFAULT), ("en-US", "english"));
    }

    #[test]
    fn language_only_match_ru_ru_to_ru() {
        assert_eq!(negotiate_locale("ru-RU", TEST_LOCALES, TEST_DEFAULT), ("ru", "russian"));
    }

    #[test]
    fn no_match_falls_back_to_default() {
        assert_eq!(negotiate_locale("ja-JP", TEST_LOCALES, TEST_DEFAULT), ("en-US", "english"));
    }

    #[test]
    fn invalid_locale_falls_back_to_default() {
        assert_eq!(negotiate_locale("!!invalid!!", TEST_LOCALES, TEST_DEFAULT), ("en-US", "english"));
    }

    #[test]
    fn empty_locale_falls_back_to_default() {
        assert_eq!(negotiate_locale("", TEST_LOCALES, TEST_DEFAULT), ("en-US", "english"));
    }

    #[test]
    fn exact_match_preferred_over_language_match() {
        let locales: &[(&str, &str)] = &[("en-US", "us"), ("en-GB", "gb")];
        assert_eq!(negotiate_locale("en-GB", locales, ("en-US", "us")), ("en-GB", "gb"));
    }

    #[test]
    fn bare_language_matches() {
        assert_eq!(negotiate_locale("en", TEST_LOCALES, TEST_DEFAULT), ("en-US", "english"));
        assert_eq!(negotiate_locale("ru", TEST_LOCALES, TEST_DEFAULT), ("ru", "russian"));
    }

    #[test]
    fn zh_hk_falls_back_to_zh_tw() {
        let locales: &[(&str, &str)] = &[("en-US", "english"), ("zh-CN", "simplified"), ("zh-TW", "traditional")];
        assert_eq!(negotiate_locale("zh-HK", locales, ("en-US", "english")), ("zh-TW", "traditional"));
    }
}
