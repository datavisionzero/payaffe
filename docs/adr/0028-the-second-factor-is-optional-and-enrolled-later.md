# The Second Factor Is Optional, and Enrolled Later

Amends [ADR 0019](./0019-the-second-factor-is-totp-and-sensitive-writes-need-a-fresh-one.md)
and [ADR 0020](./0020-the-first-admin-is-created-by-a-local-command.md).

The first Admin Account is created with a username and a password. TOTP is
something an admin adds afterwards, from the Admin UI, if they want it. An
account without it signs in on its password and may do everything.

ADR 0019 made TOTP mandatory, and ADR 0020 put the first admin's TOTP secret in
the installation's configuration under `Admin:TotpSecrets`. Together they meant
that before an installation had a single account, an operator had to generate a
secret, get it into the secret store, enrol it in an authenticator app, and type
a current code — and if any of that was off, the command failed *after* prompting
for a password and before producing anything.

That is what happened on the first real installation. Every part was correct —
valid secret, synchronised clocks, a correct RFC 6238 implementation — and the
first attempt failed anyway. A setup step with four moving parts that can fail
without producing anything is the step people abandon, and the product it guards
is the one they do not run.

The deeper mistake was where the secret lived. A TOTP secret belongs to a person.
Putting it in the deployment's configuration made three systems agree on one
value before there was an account to attach it to, and it made a personal
credential part of the installation's configuration surface.

## Consequences

**A password alone controls the installation.** This is the cost, and it is not
small: an admin can change the native ETH Address Pool, which is the one
administrative path that can point future payments at an address the operator
does not own. payaffe holds no spending key ([ADR 0005](./0005-payaffe-never-holds-a-key-that-can-spend.md)),
so nothing existing can be swept — but "cannot steal the balance" is not "cannot
take the money". Operators who care should enrol a second factor, and the
product should keep making that easy rather than mandatory.

**Step-up still applies, but only where there is something to step up with.** An
account that enrolled TOTP keeps the fresh-code requirement on sensitive writes
in full; opting in has to be worth something. An account without one is not
asked, because refusing would make those operations unreachable rather than
protected.

**A session says honestly what it cleared.** `mfa_authenticated_at` and
`step_up_authenticated_at` are nullable, and null means never. Writing the
sign-in time into them would have avoided a migration and recorded in the
security store that a factor was cleared which never was.

**`Admin:TotpSecrets` is no longer part of creating an account.** The
configuration path still resolves a secret for an account that references one,
so nothing that works today stops working. What went away is its role in
bootstrap, and with it `PAYAFFE_ADMIN_TOTP_SECRET_FIRST_ADMIN`.

**Self-service enrolment is not built yet.** Until it is, an admin who wants TOTP
still needs the configuration path, which is the arrangement this ADR is moving
away from. That is a gap, and it is tracked as work rather than presented as a
design.
