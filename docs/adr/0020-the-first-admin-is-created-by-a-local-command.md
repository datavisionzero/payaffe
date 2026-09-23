# The First Admin Is Created by a Local Command

**Amended by [ADR 0028](./0028-the-second-factor-is-optional-and-enrolled-later.md).**
The command no longer takes a TOTP secret reference or a code; it asks for a
password. Everything else below stands.

**Amended by [ADR 0037](./0037-the-first-admin-can-be-written-to-a-file-for-an-unattended-setup.md).**
Without a terminal, the command generates the password and writes it with the
Recovery Codes to a file on the host instead of prompting.

The operator runs an operations command that creates the first Admin Account. No HTTP surface creates one, no MCP tool creates one, and no host
creates one while starting.

The usual alternative is a first-run registration page guarded by a bootstrap
token, and it is refused because of what it is during the window it is open: a
publicly reachable endpoint that grants full administrative control, protected
by a secret that has to get to the operator somehow, on an installation that is
by definition not yet being watched. The window is short, the consequence of
losing the race is total, and closing the window correctly is more subtle than
it looks. Seeding credentials from compose environment variables is worse still,
since it puts an admin password into the file most likely to be committed.

Requiring the operator to insert a row with SQL was the other alternative — it
is safe and it is how the password ends up unhashed or the TOTP secret ends up
unusable, discovered at the first sign-in attempt.

The command is fail-closed. It succeeds only when no Admin Account exists, does
its check and insert in one serialized transaction so two concurrent runs cannot
both win, and afterwards refuses permanently and points at authenticated admin
management or the documented lockout recovery.

## Consequences

**The password is prompted, never passed.** It is read through a masked prompt
with confirmation and is not accepted as a command argument or an environment
variable, because both are read by anyone with the process list or the file.

**The TOTP secret is the operator's, and the database stores only a
reference.** The operator generates it, stores it through the installation's
server-side secret mechanism, and the command resolves it only from the
`Admin:TotpSecrets` subtree. It must have no value in versioned compose or
example configuration.

**Enrolment is verified before the account exists.** The command asks for a
current TOTP code and checks it, so the failure mode is "that secret does not
work, try again" rather than an account nobody can sign into.

**This requires deployment and database authority**, which is the intended
audience. It is an operator's tool, and it is documented as one in
[credential-rotation.md](../operations/credential-rotation.md).
