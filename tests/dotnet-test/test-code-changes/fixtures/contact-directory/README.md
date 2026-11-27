# Contacts

`tests/Contacts.Tests` runs in memory. `tests/Contacts.SqlTests` needs a SQL
Server connection with permission to create temporary tables. This copy uses
a fixed unavailable loopback endpoint, with a one-second connect timeout
and no retries.
