// Copyright 2023 Google LLC

// Licensed under the Apache License, Version 2.0 <LICENSE-APACHE or
// https://www.apache.org/licenses/LICENSE-2.0> or the MIT license
// <LICENSE-MIT or https://opensource.org/licenses/MIT>, at your
// option. This file may not be copied, modified, or distributed
// except according to those terms.

use std::cmp::min;

/// A struct which can issue periodic updates indicating progress towards
/// an external total, based on updates towards an internal goal.
pub struct ProgressUpdater<F: Fn(u64)> {
    callback: F,
    internal_progress: u64,
    per_update_internal: u64,
    update_external_amount: u64,
    external_updates_sent: u64,
    remainder_external: u64,
    internal_total: u64,
}

impl<F: Fn(u64)> ProgressUpdater<F> {
    /// Create a new progress updater, with a callback to be called periodically.
    pub fn new(callback: F, external_total: u64, internal_total: u64, per_update_internal: u64) -> Self {
        let per_update_internal = min(internal_total, per_update_internal);
        let total_updates_expected = if per_update_internal == 0 { 0 } else { internal_total / per_update_internal };
        let (update_external_amount, remainder_external) = if total_updates_expected == 0 {
            (0, external_total)
        } else {
            (external_total / total_updates_expected, external_total % total_updates_expected)
        };
        Self {
            callback,
            internal_progress: 0u64,
            per_update_internal,
            update_external_amount,
            external_updates_sent: 0u64,
            remainder_external,
            internal_total,
        }
    }

    /// Indicate some progress towards the internal goal. May call back the
    /// external callback function to show some progress towards the external
    /// goal.
    pub fn progress(&mut self, amount_internal: u64) {
        self.internal_progress += amount_internal;
        self.send_due_updates();
    }

    fn send_due_updates(&mut self) {
        let updates_due = if self.per_update_internal == 0 { 0 } else { self.internal_progress / self.per_update_internal };
        while updates_due > self.external_updates_sent {
            (self.callback)(self.update_external_amount);
            self.external_updates_sent += 1;
        }
    }

    /// Indicate completion of the task. Fully update the callback towards the
    /// external state.
    pub fn finish(&mut self) {
        self.internal_progress = self.internal_total;
        self.send_due_updates();
        if self.remainder_external > 0 {
            (self.callback)(self.remainder_external);
        }
    }
}

#[test]
fn test_progress_updater() {
    let amount_received = std::rc::Rc::new(std::cell::RefCell::new(0u64));
    let mut progresser = ProgressUpdater::new(
        |progress| {
            *(amount_received.borrow_mut()) += progress;
        },
        100,
        1000,
        100,
    );
    assert_eq!(*amount_received.borrow(), 0);
    progresser.progress(1);
    assert_eq!(*amount_received.borrow(), 0);
    progresser.progress(100);
    assert_eq!(*amount_received.borrow(), 10);
    progresser.progress(800);
    assert_eq!(*amount_received.borrow(), 90);
    progresser.finish();
    assert_eq!(*amount_received.borrow(), 100);
}

#[test]
fn test_progress_updater_zero_external() {
    let amount_received = std::rc::Rc::new(std::cell::RefCell::new(0u64));
    let mut progresser = ProgressUpdater::new(
        |progress| {
            *(amount_received.borrow_mut()) += progress;
        },
        0,
        1000,
        100,
    );
    assert_eq!(*amount_received.borrow(), 0);
    progresser.progress(1);
    progresser.progress(800);
    progresser.finish();
    assert_eq!(*amount_received.borrow(), 0);
}

#[test]
fn test_progress_updater_small_internal() {
    let amount_received = std::rc::Rc::new(std::cell::RefCell::new(0u64));
    let mut progresser = ProgressUpdater::new(
        |progress| {
            *(amount_received.borrow_mut()) += progress;
        },
        100,
        5,
        100,
    );
    assert_eq!(*amount_received.borrow(), 0);
    progresser.progress(1);
    progresser.finish();
    assert_eq!(*amount_received.borrow(), 100);
}

#[test]
fn test_progress_updater_zero_internal() {
    let amount_received = std::rc::Rc::new(std::cell::RefCell::new(0u64));
    let mut progresser = ProgressUpdater::new(
        |progress| {
            *(amount_received.borrow_mut()) += progress;
        },
        100,
        0,
        100,
    );
    assert_eq!(*amount_received.borrow(), 0);
    progresser.finish();
    assert_eq!(*amount_received.borrow(), 100);
}
