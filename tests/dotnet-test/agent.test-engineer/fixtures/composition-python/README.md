# Parcel

Run the existing suite with `python -B -m unittest discover -v`.
There are no third-party dependencies. Runtime modules and this runner are not
part of a test-only edit.

The shipping contract is in `shipping.quote`'s docstring; remote delivery adds
8 even when the base delivery is free. Subtotals are integer minor units;
fractional amounts and other input types are outside the documented API contract.
Stock reservations return a success
boolean and decrease quantity only on success. Missing measurements are not
zero coverage. Report files supplied for planning are not post-edit validation.
