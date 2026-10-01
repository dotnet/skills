This fixture uses per-window lifecycle callbacks.
Draft state is saved when the window stops, not during window teardown.
Draft state is restored only when a stopped window resumes.
Irreplaceable edits must be persisted incrementally because abrupt process death may deliver no final callback.
