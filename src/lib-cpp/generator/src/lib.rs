mod cpp_glue;
pub use crate::cpp_glue::*;

use rifgen::rifgen_attr::*;

extern crate velopack;

velopack::

pub struct Foo {
    data: i32,
}

impl Foo {
    #[generate_interface(constructor)]
    fn new(val: i32) -> Foo {
        Foo { data: val }
    }
    #[generate_interface]
    fn f(&self, a: i32, b: i32) -> i32 {
        self.data + a + b
    }

    ///Custom doc comment
    #[generate_interface]
    fn set_field(&mut self, v: i32) {
        self.data = v;
    }
}

fn f2(a: i32) -> i32 {
    a * 2
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn it_works() {
        let foo = Foo::new(5);
        assert_eq!(8, foo.f(1, 2));
    }
}

