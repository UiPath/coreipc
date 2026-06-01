//! Double-encode / decode helpers and the [`EncodeArgs`] tuple conversion.
//!
//! On the wire, each request parameter and each response `Data` value is *itself* a JSON string
//! (JSON-within-JSON). These helpers perform that inner encode/decode.
//!
//! ## DTO serialization convention
//!
//! The deeper DTO payloads must match .NET's serializer (Newtonsoft.Json): **PascalCase member
//! names, enums-as-string, ISO-8601 dates, null/default omission**. serde has no global casing
//! switch, so shipped DTOs encode the convention per-type with serde attributes, e.g.:
//!
//! ```ignore
//! #[derive(Serialize, Deserialize)]
//! #[serde(rename_all = "PascalCase")]
//! struct Dto { bool_property: bool, int_property: i32, string_property: String }
//! ```

use serde::{de::DeserializeOwned, Serialize};

/// Double-encode a single argument: serialize it to a JSON string.
///
/// `encode_arg(&2) == "2"`, `encode_arg(&"hi") == "\"hi\""`.
pub fn encode_arg<T: Serialize + ?Sized>(value: &T) -> Result<String, serde_json::Error> {
    serde_json::to_string(value)
}

/// Decode a return value from the optional double-encoded `Data` field.
///
/// `None` and an empty string both decode as JSON `null` (so `()`/`Option` round-trip for
/// void/null returns). Otherwise the string is parsed as the inner JSON of `T`.
pub fn decode_ret<T: DeserializeOwned>(data: Option<&str>) -> Result<T, serde_json::Error> {
    match data {
        None | Some("") => serde_json::from_str("null"),
        Some(s) => serde_json::from_str(s),
    }
}

/// Conversion of a call's arguments into the double-encoded `Parameters` vector.
///
/// Implemented for tuples (arities 0–8) so positional .NET arguments map naturally:
/// `("IAlgebra","MultiplySimple")` with args `(2, 3)` produces `["2", "3"]`. A
/// `Vec<serde_json::Value>` escape hatch covers dynamic/variadic cases.
pub trait EncodeArgs {
    fn encode_args(&self) -> Result<Vec<String>, serde_json::Error>;
}

impl EncodeArgs for () {
    fn encode_args(&self) -> Result<Vec<String>, serde_json::Error> {
        Ok(Vec::new())
    }
}

impl EncodeArgs for Vec<serde_json::Value> {
    fn encode_args(&self) -> Result<Vec<String>, serde_json::Error> {
        self.iter().map(serde_json::to_string).collect()
    }
}

macro_rules! impl_encode_args_tuple {
    ( $( $name:ident ),+ ) => {
        impl<$( $name: Serialize ),+> EncodeArgs for ( $( $name, )+ ) {
            fn encode_args(&self) -> Result<Vec<String>, serde_json::Error> {
                #[allow(non_snake_case)]
                let ( $( $name, )+ ) = self;
                Ok(vec![ $( serde_json::to_string($name)? ),+ ])
            }
        }
    };
}

impl_encode_args_tuple!(A);
impl_encode_args_tuple!(A, B);
impl_encode_args_tuple!(A, B, C);
impl_encode_args_tuple!(A, B, C, D);
impl_encode_args_tuple!(A, B, C, D, E);
impl_encode_args_tuple!(A, B, C, D, E, F);
impl_encode_args_tuple!(A, B, C, D, E, F, G);
impl_encode_args_tuple!(A, B, C, D, E, F, G, H);
