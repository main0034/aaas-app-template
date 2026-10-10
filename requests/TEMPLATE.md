<!--
A request for one change to this application. Copy this file to
requests/<short-name>.md (lower case, dashes: requests/item-archive.md), fill it in,
commit it, then start the change:

    ./run.sh --task create-change --app-repo <this repo> --change <short-name>

Two agents read it. One builds the change. The other, who never sees the code,
writes the acceptance tests that decide whether the change is accepted. Neither can
ask you anything, so everything they need has to be on this page. Write in your own
words; there is no need to know how the app is built.

Delete these comments and any section that does not apply. A small change can be
five lines. "Rules" and "Interface" matter most: they are what the tests check.
-->

# <What the change is, in a few words>

## What and why

<!-- One or two sentences: what you want to be able to do, and why. "We want to see
what is coming up this week, so nothing is missed." -->

## Rules

<!-- Each rule is one statement that is either true or false of the finished app.
Be exact where it matters:

- Limits: "1 to 100 characters", "at most 10", "between 0 and 365". Say which ends
  are included.
- Before or after a change to the value: "100 characters after trimming spaces".
- Dates and time: "today counts", "up to and including the 7th day", "today is the
  UTC date".
- Order: what comes first, and what decides a tie ("newest first", "the one added
  first").
- What is left out: done items, items without a date, anything else.
- A rule about a group applies to every member, so say so for done items too: "a
  project can be deleted only when it has no items, done or not".
- What happens to a refused request: "refused, and nothing changes".
- Existing data: "existing items keep their data". -->

-
-

## Interface (fixed - build exactly this)

<!-- The routes, exactly. For each: method and path, parameters, the request body,
the status codes (success, not found, invalid, conflict) and what comes back, with a
small example. "In the same shape GET /items returns them" is enough when it is. -->

- `GET /...` returns `200` with `...`. Unknown ...: `404`. Invalid ...: `400`.

## Examples

<!-- Optional, and the best way to remove doubt. A few concrete cases, each a
situation, an action and the expected result:

- Items due today, tomorrow and in 8 days; `GET /items/upcoming?days=7` returns the
  first two, today's first. -->

## Not part of this change

<!-- Optional. Anything someone might reasonably assume is included but is not:
"no editing a project's name yet". -->

## Open questions

<!-- Optional. Anything you are unsure about. Both agents leave it alone and say so in
the pull request, rather than guess. -->
