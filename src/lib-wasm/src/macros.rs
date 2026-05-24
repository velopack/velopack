/// Defines a struct with case-insensitive JSON deserialization.
/// All incoming JSON keys are lowercased before matching against field names.
/// All field types must implement `serde::de::DeserializeOwned` and `Default`.
/// Adapted from the serde-aux crate's `deserialize_struct_case_insensitive`.
macro_rules! define_struct_case_insensitive {
    (
        $(#[$meta:meta])*
        $vis:vis struct $name:ident {
            $(
                $(#[$field_meta:meta])*
                $field_vis:vis $field:ident : $ty:ty,
            )*
        }
    ) => {
        $(#[$meta])*
        $vis struct $name {
            $(
                $(#[$field_meta])*
                $field_vis $field: $ty,
            )*
        }

        impl<'de> serde::Deserialize<'de> for $name {
            fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
            where
                D: serde::Deserializer<'de>,
            {
                use std::collections::BTreeMap;
                let map = BTreeMap::<String, serde_json::Value>::deserialize(deserializer)?;
                let lower: serde_json::Map<String, serde_json::Value> =
                    map.into_iter().map(|(k, v)| (k.to_lowercase(), v)).collect();
                let mut result = $name::default();
                $(
                    if let Some(v) = lower.get(&stringify!($field).to_lowercase()) {
                        result.$field = serde_json::from_value(v.clone()).map_err(serde::de::Error::custom)?;
                    }
                )*
                Ok(result)
            }
        }
    };
}
