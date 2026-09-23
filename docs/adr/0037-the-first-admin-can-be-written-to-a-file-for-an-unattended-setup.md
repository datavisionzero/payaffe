# The First Admin Can Be Written to a File, for an Unattended Setup

Amends [ADR 0020](./0020-the-first-admin-is-created-by-a-local-command.md).

`bootstrap-admin --credentials-file <path>` creates the first Admin Account
without a terminal. The command generates the password itself and writes it,
with the Recovery Codes, to a file it creates exclusively and readable by its
owner only, on a host directory mounted into the one-off container. Its output
names the account and the file and carries nothing secret.

An installation is more and more often set up by an agent working on the host
for a person, and ADR 0020 left that agent one step it could not take: a masked
prompt needs a terminal, and the command refused to run without one. The ways
around it were all worse than the rule. Faking a terminal to type into the
prompt means the agent chose the password and saw the Recovery Codes, which
then sit in its transcript, with its model provider. Inserting the account with
SQL is the unhashed password ADR 0020 refused.

The other products of this family bootstrap through a token in the
configuration that a browser exchanges once for a password. That is the
first-run endpoint ADR 0020 refused for payaffe, and it is not reopened here.

## Consequences

**The secrets are in a file until a person deletes them.** That is the cost.
The file is created with mode `0600` and never overwritten, and whoever can read
it on the host can already read the database. The documented procedure tells
the agent to name the file to the person rather than read it, and tells the
person to move both secrets into a password manager and delete it.

**Nobody types the password, so nobody can choose a weak one.** It is 32
characters from an alphabet without look-alike characters.

**The file exists before the account does.** A path that cannot be written, or
already holds a file, stops the command before anything is created, so an
account whose only copy of its password went nowhere is not a failure this can
produce on a bad mount. A run that creates nothing removes the file again.

**The interactive command is unchanged.** Without the option it still needs a
terminal and still refuses redirected input and output.
