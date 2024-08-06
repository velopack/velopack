#[allow(unused_macros)]
macro_rules! swig_c_str {
    ($ lit : expr) => {
        concat!($lit, "\0").as_ptr() as *const ::std::os::raw::c_char
    };
}
#[allow(dead_code)]
pub trait SwigForeignClass {
    fn c_class_name() -> *const ::std::os::raw::c_char;
    fn box_object(x: Self) -> *mut ::std::os::raw::c_void;
    fn unbox_object(p: *mut ::std::os::raw::c_void) -> Self;
}
#[allow(dead_code)]
pub trait SwigForeignEnum {
    fn as_u32(&self) -> u32;
    fn from_u32(_: u32) -> Self;
}
#[allow(dead_code)]
#[doc = ""]
trait SwigFrom<T> {
    fn swig_from(_: T) -> Self;
}
#[allow(dead_code)]
#[repr(C)]
#[derive(Clone, Copy)]
pub struct CRustStrView {
    data: *const ::std::os::raw::c_char,
    len: usize,
}
#[allow(dead_code)]
impl CRustStrView {
    fn from_str(s: &str) -> CRustStrView {
        CRustStrView { data: s.as_ptr() as *const ::std::os::raw::c_char, len: s.len() }
    }
}
#[allow(dead_code)]
#[repr(C)]
#[derive(Copy, Clone)]
pub struct CRustString {
    data: *const ::std::os::raw::c_char,
    len: usize,
    capacity: usize,
}
#[allow(dead_code)]
impl CRustString {
    pub fn from_string(s: String) -> CRustString {
        let data = s.as_ptr() as *const ::std::os::raw::c_char;
        let len = s.len();
        let capacity = s.capacity();
        ::std::mem::forget(s);
        CRustString { data, len, capacity }
    }
}
#[allow(dead_code)]
#[repr(C)]
#[derive(Clone, Copy)]
pub struct CRustObjectSlice {
    data: *const ::std::os::raw::c_void,
    len: usize,
    step: usize,
}
#[allow(dead_code)]
#[repr(C)]
pub struct CRustObjectMutSlice {
    data: *mut ::std::os::raw::c_void,
    len: usize,
    step: usize,
}
#[allow(dead_code)]
#[repr(C)]
#[derive(Clone, Copy)]
pub struct CRustSliceAccess {
    data: *const ::std::os::raw::c_void,
    len: usize,
}
#[allow(dead_code)]
impl CRustSliceAccess {
    pub fn from_slice<T>(sl: &[T]) -> Self {
        Self { data: sl.as_ptr() as *const ::std::os::raw::c_void, len: sl.len() }
    }
}
#[allow(dead_code)]
#[repr(C)]
#[derive(Copy, Clone)]
pub struct CRustVecAccess {
    data: *const ::std::os::raw::c_void,
    len: usize,
    capacity: usize,
}
#[allow(dead_code)]
impl CRustVecAccess {
    pub fn from_vec<T>(mut v: Vec<T>) -> Self {
        let data = v.as_mut_ptr() as *const ::std::os::raw::c_void;
        let len = v.len();
        let capacity = v.capacity();
        ::std::mem::forget(v);
        Self { data, len, capacity }
    }
    pub fn to_slice<'a, T>(cs: Self) -> &'a [T] {
        unsafe { ::std::slice::from_raw_parts(cs.data as *const T, cs.len) }
    }
    pub fn to_vec<T>(cs: Self) -> Vec<T> {
        unsafe { Vec::from_raw_parts(cs.data as *mut T, cs.len, cs.capacity) }
    }
}
#[allow(dead_code)]
#[repr(C)]
#[derive(Copy, Clone)]
pub struct CRustForeignVec {
    data: *const ::std::os::raw::c_void,
    len: usize,
    capacity: usize,
}
#[allow(dead_code)]
impl CRustForeignVec {
    pub fn from_vec<T: SwigForeignClass>(mut v: Vec<T>) -> CRustForeignVec {
        let data = v.as_mut_ptr() as *const ::std::os::raw::c_void;
        let len = v.len();
        let capacity = v.capacity();
        ::std::mem::forget(v);
        CRustForeignVec { data, len, capacity }
    }
}
#[allow(dead_code)]
#[inline]
fn push_foreign_class_to_vec<T: SwigForeignClass>(vec: *mut CRustForeignVec, elem: *mut ::std::os::raw::c_void) {
    assert!(!vec.is_null());
    let vec: &mut CRustForeignVec = unsafe { &mut *vec };
    let mut v = unsafe { Vec::from_raw_parts(vec.data as *mut T, vec.len, vec.capacity) };
    v.push(T::unbox_object(elem));
    vec.data = v.as_mut_ptr() as *const ::std::os::raw::c_void;
    vec.len = v.len();
    vec.capacity = v.capacity();
    ::std::mem::forget(v);
}
#[allow(dead_code)]
#[inline]
fn remove_foreign_class_from_vec<T: SwigForeignClass>(vec: *mut CRustForeignVec, index: usize) -> *mut ::std::os::raw::c_void {
    assert!(!vec.is_null());
    let vec: &mut CRustForeignVec = unsafe { &mut *vec };
    let mut v = unsafe { Vec::from_raw_parts(vec.data as *mut T, vec.len, vec.capacity) };
    let elem: T = v.remove(index);
    vec.data = v.as_mut_ptr() as *const ::std::os::raw::c_void;
    vec.len = v.len();
    vec.capacity = v.capacity();
    ::std::mem::forget(v);
    T::box_object(elem)
}
#[allow(dead_code)]
#[inline]
fn drop_foreign_class_vec<T: SwigForeignClass>(v: CRustForeignVec) {
    let v = unsafe { Vec::from_raw_parts(v.data as *mut T, v.len, v.capacity) };
    drop(v);
}
use crate::*;
#[allow(non_snake_case)]
#[test]
fn test_CRustStrView_layout() {
    #[repr(C)]
    struct MyCRustStrView {
        data: *const ::std::os::raw::c_char,
        len: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustStrView>(), ::std::mem::size_of::<CRustStrView>());
    assert_eq!(::std::mem::align_of::<MyCRustStrView>(), ::std::mem::align_of::<CRustStrView>());
    let our_s: MyCRustStrView = unsafe { ::std::mem::zeroed() };
    let user_s: CRustStrView = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustStrView_data_type_fn(s: &CRustStrView) -> &*const ::std::os::raw::c_char {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *const ::std::os::raw::c_char) as usize) - ((&our_s as *const MyCRustStrView) as usize);
    let offset_user = ((&user_s.data as *const *const ::std::os::raw::c_char) as usize) - ((&user_s as *const CRustStrView) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustStrView_len_type_fn(s: &CRustStrView) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustStrView) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustStrView) as usize);
    assert_eq!(offset_our, offset_user);
}
#[allow(non_snake_case)]
#[test]
fn test_CRustString_layout() {
    #[repr(C)]
    struct MyCRustString {
        data: *const ::std::os::raw::c_char,
        len: usize,
        capacity: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustString>(), ::std::mem::size_of::<CRustString>());
    assert_eq!(::std::mem::align_of::<MyCRustString>(), ::std::mem::align_of::<CRustString>());
    let our_s: MyCRustString = unsafe { ::std::mem::zeroed() };
    let user_s: CRustString = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustString_data_type_fn(s: &CRustString) -> &*const ::std::os::raw::c_char {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *const ::std::os::raw::c_char) as usize) - ((&our_s as *const MyCRustString) as usize);
    let offset_user = ((&user_s.data as *const *const ::std::os::raw::c_char) as usize) - ((&user_s as *const CRustString) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustString_len_type_fn(s: &CRustString) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustString) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustString) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustString_capacity_type_fn(s: &CRustString) -> &usize {
        &s.capacity
    }
    let offset_our = ((&our_s.capacity as *const usize) as usize) - ((&our_s as *const MyCRustString) as usize);
    let offset_user = ((&user_s.capacity as *const usize) as usize) - ((&user_s as *const CRustString) as usize);
    assert_eq!(offset_our, offset_user);
}
#[no_mangle]
pub extern "C" fn crust_string_free(x: CRustString) {
    let s = unsafe { String::from_raw_parts(x.data as *mut u8, x.len, x.capacity) };
    drop(s);
}
#[no_mangle]
pub extern "C" fn crust_string_clone(x: CRustString) -> CRustString {
    let s = unsafe { String::from_raw_parts(x.data as *mut u8, x.len, x.capacity) };
    let ret = CRustString::from_string(s.clone());
    ::std::mem::forget(s);
    ret
}
#[allow(non_snake_case)]
#[test]
fn test_CRustObjectSlice_layout() {
    #[repr(C)]
    struct MyCRustObjectSlice {
        data: *const ::std::os::raw::c_void,
        len: usize,
        step: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustObjectSlice>(), ::std::mem::size_of::<CRustObjectSlice>());
    assert_eq!(::std::mem::align_of::<MyCRustObjectSlice>(), ::std::mem::align_of::<CRustObjectSlice>());
    let our_s: MyCRustObjectSlice = unsafe { ::std::mem::zeroed() };
    let user_s: CRustObjectSlice = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustObjectSlice_data_type_fn(s: &CRustObjectSlice) -> &*const ::std::os::raw::c_void {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&our_s as *const MyCRustObjectSlice) as usize);
    let offset_user = ((&user_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&user_s as *const CRustObjectSlice) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustObjectSlice_len_type_fn(s: &CRustObjectSlice) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustObjectSlice) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustObjectSlice) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustObjectSlice_step_type_fn(s: &CRustObjectSlice) -> &usize {
        &s.step
    }
    let offset_our = ((&our_s.step as *const usize) as usize) - ((&our_s as *const MyCRustObjectSlice) as usize);
    let offset_user = ((&user_s.step as *const usize) as usize) - ((&user_s as *const CRustObjectSlice) as usize);
    assert_eq!(offset_our, offset_user);
}
#[allow(non_snake_case)]
#[test]
fn test_CRustObjectMutSlice_layout() {
    #[repr(C)]
    struct MyCRustObjectMutSlice {
        data: *mut ::std::os::raw::c_void,
        len: usize,
        step: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustObjectMutSlice>(), ::std::mem::size_of::<CRustObjectMutSlice>());
    assert_eq!(::std::mem::align_of::<MyCRustObjectMutSlice>(), ::std::mem::align_of::<CRustObjectMutSlice>());
    let our_s: MyCRustObjectMutSlice = unsafe { ::std::mem::zeroed() };
    let user_s: CRustObjectMutSlice = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustObjectMutSlice_data_type_fn(s: &CRustObjectMutSlice) -> &*mut ::std::os::raw::c_void {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *mut ::std::os::raw::c_void) as usize) - ((&our_s as *const MyCRustObjectMutSlice) as usize);
    let offset_user = ((&user_s.data as *const *mut ::std::os::raw::c_void) as usize) - ((&user_s as *const CRustObjectMutSlice) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustObjectMutSlice_len_type_fn(s: &CRustObjectMutSlice) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustObjectMutSlice) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustObjectMutSlice) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustObjectMutSlice_step_type_fn(s: &CRustObjectMutSlice) -> &usize {
        &s.step
    }
    let offset_our = ((&our_s.step as *const usize) as usize) - ((&our_s as *const MyCRustObjectMutSlice) as usize);
    let offset_user = ((&user_s.step as *const usize) as usize) - ((&user_s as *const CRustObjectMutSlice) as usize);
    assert_eq!(offset_our, offset_user);
}
#[allow(non_snake_case)]
#[test]
fn test_CRustSliceAccess_layout() {
    #[repr(C)]
    struct MyCRustSliceAccess {
        data: *const ::std::os::raw::c_void,
        len: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustSliceAccess>(), ::std::mem::size_of::<CRustSliceAccess>());
    assert_eq!(::std::mem::align_of::<MyCRustSliceAccess>(), ::std::mem::align_of::<CRustSliceAccess>());
    let our_s: MyCRustSliceAccess = unsafe { ::std::mem::zeroed() };
    let user_s: CRustSliceAccess = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustSliceAccess_data_type_fn(s: &CRustSliceAccess) -> &*const ::std::os::raw::c_void {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&our_s as *const MyCRustSliceAccess) as usize);
    let offset_user = ((&user_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&user_s as *const CRustSliceAccess) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustSliceAccess_len_type_fn(s: &CRustSliceAccess) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustSliceAccess) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustSliceAccess) as usize);
    assert_eq!(offset_our, offset_user);
}
#[allow(non_snake_case)]
#[test]
fn test_CRustVecAccess_layout() {
    #[repr(C)]
    struct MyCRustVecAccess {
        data: *const ::std::os::raw::c_void,
        len: usize,
        capacity: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustVecAccess>(), ::std::mem::size_of::<CRustVecAccess>());
    assert_eq!(::std::mem::align_of::<MyCRustVecAccess>(), ::std::mem::align_of::<CRustVecAccess>());
    let our_s: MyCRustVecAccess = unsafe { ::std::mem::zeroed() };
    let user_s: CRustVecAccess = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustVecAccess_data_type_fn(s: &CRustVecAccess) -> &*const ::std::os::raw::c_void {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&our_s as *const MyCRustVecAccess) as usize);
    let offset_user = ((&user_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&user_s as *const CRustVecAccess) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustVecAccess_len_type_fn(s: &CRustVecAccess) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustVecAccess) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustVecAccess) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustVecAccess_capacity_type_fn(s: &CRustVecAccess) -> &usize {
        &s.capacity
    }
    let offset_our = ((&our_s.capacity as *const usize) as usize) - ((&our_s as *const MyCRustVecAccess) as usize);
    let offset_user = ((&user_s.capacity as *const usize) as usize) - ((&user_s as *const CRustVecAccess) as usize);
    assert_eq!(offset_our, offset_user);
}
#[allow(non_snake_case)]
#[test]
fn test_CRustForeignVec_layout() {
    #[repr(C)]
    struct MyCRustForeignVec {
        data: *const ::std::os::raw::c_void,
        len: usize,
        capacity: usize,
    }
    assert_eq!(::std::mem::size_of::<MyCRustForeignVec>(), ::std::mem::size_of::<CRustForeignVec>());
    assert_eq!(::std::mem::align_of::<MyCRustForeignVec>(), ::std::mem::align_of::<CRustForeignVec>());
    let our_s: MyCRustForeignVec = unsafe { ::std::mem::zeroed() };
    let user_s: CRustForeignVec = unsafe { ::std::mem::zeroed() };
    #[allow(dead_code)]
    fn check_CRustForeignVec_data_type_fn(s: &CRustForeignVec) -> &*const ::std::os::raw::c_void {
        &s.data
    }
    let offset_our = ((&our_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&our_s as *const MyCRustForeignVec) as usize);
    let offset_user = ((&user_s.data as *const *const ::std::os::raw::c_void) as usize) - ((&user_s as *const CRustForeignVec) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustForeignVec_len_type_fn(s: &CRustForeignVec) -> &usize {
        &s.len
    }
    let offset_our = ((&our_s.len as *const usize) as usize) - ((&our_s as *const MyCRustForeignVec) as usize);
    let offset_user = ((&user_s.len as *const usize) as usize) - ((&user_s as *const CRustForeignVec) as usize);
    assert_eq!(offset_our, offset_user);
    #[allow(dead_code)]
    fn check_CRustForeignVec_capacity_type_fn(s: &CRustForeignVec) -> &usize {
        &s.capacity
    }
    let offset_our = ((&our_s.capacity as *const usize) as usize) - ((&our_s as *const MyCRustForeignVec) as usize);
    let offset_user = ((&user_s.capacity as *const usize) as usize) - ((&user_s as *const CRustForeignVec) as usize);
    assert_eq!(offset_our, offset_user);
}
impl SwigForeignClass for Foo {
    fn c_class_name() -> *const ::std::os::raw::c_char {
        swig_c_str!(stringify!(Foo))
    }
    fn box_object(this: Self) -> *mut ::std::os::raw::c_void {
        let this: Box<Foo> = Box::new(this);
        let this: *mut Foo = Box::into_raw(this);
        this as *mut ::std::os::raw::c_void
    }
    fn unbox_object(p: *mut ::std::os::raw::c_void) -> Self {
        let p = p as *mut Foo;
        let p: Box<Foo> = unsafe { Box::from_raw(p) };
        let p: Foo = *p;
        p
    }
}
#[allow(unused_variables, unused_mut, non_snake_case, unused_unsafe)]
#[no_mangle]
pub extern "C" fn Foo_new(val: i32) -> *const ::std::os::raw::c_void {
    let this: Foo = Foo::new(val);
    let this: Box<Foo> = Box::new(this);
    let this: *mut Foo = Box::into_raw(this);
    this as *const ::std::os::raw::c_void
}
#[allow(non_snake_case, unused_variables, unused_mut, unused_unsafe)]
#[no_mangle]
pub extern "C" fn Foo_f(this: *mut Foo, a: i32, b: i32) -> i32 {
    let this: &Foo = unsafe { this.as_mut().unwrap() };
    let mut ret: i32 = Foo::f(this, a, b);
    ret
}
#[allow(non_snake_case, unused_variables, unused_mut, unused_unsafe)]
#[no_mangle]
pub extern "C" fn Foo_setField(this: *mut Foo, v: i32) -> () {
    let this: &mut Foo = unsafe { this.as_mut().unwrap() };
    let mut ret: () = Foo::set_field(this, v);
    ret
}
#[allow(unused_variables, unused_mut, non_snake_case, unused_unsafe)]
#[no_mangle]
pub extern "C" fn Foo_delete(this: *mut Foo) {
    let this: Box<Foo> = unsafe { Box::from_raw(this) };
    drop(this);
}
